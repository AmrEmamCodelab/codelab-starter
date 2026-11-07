import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { DestroyRef, ErrorHandler, Injectable, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { SessionKeyStore, type SigningSessionBlock } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { AuthClient } from '../api/clients/auth.client';
import type { LoginRequest, SessionResponse } from '../api/clients/api.models';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { loginTree, safeReturnUrl } from '../routing/route-trees';
import { SessionEndReasons, type SessionEndReason } from '../routing/route-paths';
import { AppStorage } from '../session/app-storage';
import { BrowserLocation } from '../session/browser-location';
import { APP_CLOCK } from '../session/clock';
import { StorageKeys } from '../session/storage-keys';
import { MILLISECONDS_PER_MINUTE } from '../session/time-units';
import { CROSS_TAB_LOCK, CrossTabLockNames } from './cross-tab-lock';
import { SignInMode } from './sign-in-mode';
import { SsoBindingStore } from './sso-binding';
import { TokenStore } from './token.store';

/** How long before the access token expires the browser refreshes it. */
export const ACCESS_TOKEN_REFRESH_LEAD_MS = MILLISECONDS_PER_MINUTE;

/**
 * The session lifecycle: sign-in (basic, Active Directory or SSO), proactive token refresh, logout and expiry.
 *
 * - Every sign-in method ends in the same session body; the store remembers which {@link SignInMode} started it.
 * - SSO is two top-level navigations: {@link startSsoSignIn} leaves for the API (which redirects to the identity
 *   provider), and the callback page hands the one-time ticket to {@link completeSsoSignIn}.
 * - Logout of an SSO session calls `api/auth/sso/logout` and then navigates the whole page to the identity
 *   provider's end-session URL (a GET carrying id_token_hint, so the provider redirects back); basic and AD
 *   sessions call `api/auth/logout` and go to the login page.
 * - Login adopts the request-signing keys (the login response's `signing` block, else the block of
 *   `GET api/security/session-signing-key`, where a 404 means the server does not sign), THEN stores the tokens,
 *   so no caller sees a session that cannot sign yet, and syncs the permissions. Its failures propagate unchanged (an `HttpErrorResponse` the login form
 *   parses with `parseApiError`, or an ng-security signing error).
 * - The access token is refreshed one minute before it expires. A failed refresh (refused, unverifiable, or its new
 *   signing keys refused) is reported to the `ErrorHandler` and expires the session: the user returns to the login
 *   page with the `expired` notice and the current URL, never to a page that only looks signed in.
 *   Tabs share the session and the refresh token is single-use, so a refresh runs under a cross-tab lock
 *   and first adopts the stored tokens: when another tab already rotated them, this tab takes the new
 *   tokens and sends nothing (re-sending a rotated secret makes the server revoke the session).
 * - Ending a session clears the tokens, the signing keys and the permissions.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly client = inject(AuthClient);
  private readonly tokens = inject(TokenStore);
  private readonly permissions = inject(PermissionSyncService);
  private readonly router = inject(Router);
  private readonly clock = inject(APP_CLOCK);
  private readonly signingKeys = inject(SessionKeyStore, { optional: true });
  private readonly lock = inject(CROSS_TAB_LOCK);
  private readonly storage = inject(AppStorage);
  private readonly browser = inject(BrowserLocation);
  private readonly errors = inject(ErrorHandler);
  private readonly ssoBindings = inject(SsoBindingStore);

  private refreshTimer: ReturnType<typeof setTimeout> | undefined;
  private pendingRefresh: Promise<boolean> | null = null;

  readonly isAuthenticated = computed(() => this.tokens.session() !== null);
  readonly user = this.tokens.user;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.cancelRefresh());
    if (this.tokens.session()) {
      this.scheduleRefresh();
    }
  }

  /**
   * Signs in with a user name and password and loads the user's permissions.
   *
   * @throws the login failure; nothing is stored when it throws.
   */
  async login(userName: string, password: string): Promise<void> {
    const request: LoginRequest = { userName, password };
    await this.beginSession(await firstValueFrom(this.client.login(request)), SignInMode.Basic);
  }

  /**
   * Signs in with an Active Directory account (`api/auth/ad/login`); otherwise exactly {@link login}.
   *
   * @throws the login failure; nothing is stored when it throws.
   */
  async loginWithDirectory(userName: string, password: string): Promise<void> {
    const request: LoginRequest = { userName, password };
    await this.beginSession(await firstValueFrom(this.client.directoryLogin(request)), SignInMode.ActiveDirectory);
  }

  /**
   * Leaves the app for the SSO sign-in (a top-level navigation to the API, which redirects to the identity
   * provider). The safe return URL is remembered locally and also sent to the API, with the hash of a fresh
   * browser-binding secret kept in this tab (login-CSRF defence, see `sso-binding.ts`).
   *
   * @returns false when the binding could not be made or the browser refused the navigation (the app stays on
   *   the login page).
   */
  async startSsoSignIn(returnUrl: string | null): Promise<boolean> {
    const binding = await this.ssoBindings.begin();
    if (binding === null) {
      return false;
    }
    const target = safeReturnUrl(returnUrl);
    if (target === null) {
      this.storage.remove(StorageKeys.SsoReturnUrl);
    } else {
      this.storage.write(StorageKeys.SsoReturnUrl, target);
    }
    return this.browser.assign(this.client.ssoLoginUrl(target, binding));
  }

  /** The binding secret of the SSO sign-in this tab started, read once (it is removed); null when there is none. */
  takeSsoBindingProof(): string | null {
    return this.ssoBindings.take();
  }

  /**
   * Swaps the one-time SSO callback ticket for a session (`api/auth/sso/exchange`).
   *
   * @param bindingProof the secret {@link takeSsoBindingProof} returned for this tab.
   * @throws the exchange failure; nothing is stored when it throws.
   */
  async completeSsoSignIn(ticket: string, bindingProof: string): Promise<void> {
    await this.beginSession(await firstValueFrom(this.client.ssoExchange({ ticket, bindingProof })), SignInMode.Sso);
  }

  /** The return URL {@link startSsoSignIn} remembered, read once (it is removed) and checked again. */
  takeSsoReturnUrl(): string | null {
    const stored = this.storage.read(StorageKeys.SsoReturnUrl);
    this.storage.remove(StorageKeys.SsoReturnUrl);
    return safeReturnUrl(stored);
  }

  /**
   * Rotates the tokens with the refresh token. Concurrent calls share one request.
   *
   * @returns false (and ends the session) when the server refuses the refresh token.
   */
  refresh(): Promise<boolean> {
    this.pendingRefresh ??= this.runRefresh().finally(() => {
      this.pendingRefresh = null;
    });
    return this.pendingRefresh;
  }

  /**
   * Revokes the session on the server (best effort) and clears it locally. An SSO session then leaves for the
   * identity provider's end-session page when the API returned one; every other session goes to the login page.
   */
  async logout(): Promise<void> {
    const endSessionUrl = this.isAuthenticated() ? await this.revokeOnServer() : null;
    await this.endSession();
    if (endSessionUrl !== null && this.browser.assign(endSessionUrl)) {
      return;
    }
    await this.router.navigateByUrl(loginTree(this.router));
  }

  /** Ends a session the server or the idle timer gave up on, returning to the login page with a notice. */
  async expireSession(reason: SessionEndReason, returnUrl?: string): Promise<void> {
    await this.endSession();
    await this.router.navigateByUrl(loginTree(this.router, returnUrl, reason));
  }

  /** Ends the server session; resolves to the end-session URL of an SSO session, or null. Never throws. */
  private async revokeOnServer(): Promise<string | null> {
    try {
      if (this.tokens.signInMode() === SignInMode.Sso) {
        return await firstValueFrom(this.client.ssoLogout());
      }
      await firstValueFrom(this.client.logout());
    } catch {
      // The server session may already be gone; the local session is cleared regardless.
    }
    return null;
  }

  private async beginSession(response: SessionResponse, mode: SignInMode): Promise<void> {
    await this.adoptSession(response, mode);
    try {
      await this.permissions.refresh();
    } catch {
      // The session is valid without the keys; the guards retry the sync before they decide.
    }
  }

  private runRefresh(): Promise<boolean> {
    return this.lock(CrossTabLockNames.SessionRefresh, () => this.refreshOnce());
  }

  /** Runs under the cross-tab lock: adopts another tab's rotation, else rotates the tokens itself. */
  private async refreshOnce(): Promise<boolean> {
    const held = this.tokens.refreshToken();
    const refreshToken = this.tokens.adoptStored()?.refreshToken;
    if (!refreshToken) {
      return false;
    }
    if (held !== null && refreshToken !== held) {
      this.scheduleRefresh();
      return true;
    }
    const mode = this.tokens.signInMode() ?? SignInMode.Basic;
    try {
      await this.adoptSession(await firstValueFrom(this.client.refresh(refreshToken)), mode);
    } catch (error) {
      this.errors.handleError(error);
      await this.expireSession(SessionEndReasons.Expired, this.router.url);
      return false;
    }
    return true;
  }

  /**
   * Adopts the signing keys, THEN publishes the session. Everything that follows `TokenStore.session()` (the
   * notifications feed, the permission sync, any effect on {@link isAuthenticated}) calls the API the moment the
   * session appears; published before its keys, those calls went out unsigned during the key round trip
   * (401 `signature_missing`) and expired the new session. The key request carries the new access token explicitly.
   */
  private async adoptSession(response: SessionResponse, mode: SignInMode): Promise<void> {
    try {
      await this.adoptSigningKeys(response);
    } catch (error) {
      this.tokens.clear();
      throw error;
    }
    this.tokens.save(response, mode);
    this.scheduleRefresh();
  }

  private async adoptSigningKeys(response: SessionResponse): Promise<void> {
    if (!this.signingKeys) return;
    const block = response.signing ?? (await this.fetchSigningBlock(response.accessToken));
    if (block) await this.signingKeys.adopt(block);
  }

  /** The session's signing block, or null when the server does not sign (404). */
  private async fetchSigningBlock(accessToken: string): Promise<SigningSessionBlock | null> {
    try {
      return await firstValueFrom(this.client.signingKey(accessToken));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === HttpStatusCode.NotFound) return null;
      throw error;
    }
  }

  private async endSession(): Promise<void> {
    this.cancelRefresh();
    this.tokens.clear();
    this.permissions.clear();
    await this.signingKeys?.clear();
  }

  private scheduleRefresh(): void {
    this.cancelRefresh();
    const session = this.tokens.session();
    if (!session) {
      return;
    }
    const delay = Math.max(0, session.accessTokenExpiresAt - ACCESS_TOKEN_REFRESH_LEAD_MS - this.clock());
    this.refreshTimer = setTimeout(() => void this.refresh(), delay);
  }

  private cancelRefresh(): void {
    if (this.refreshTimer !== undefined) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = undefined;
    }
  }
}
