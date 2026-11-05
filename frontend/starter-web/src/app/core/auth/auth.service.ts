import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { SessionKeyStore, type SigningSessionBlock } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { AuthClient } from '../api/clients/auth.client';
import type { SessionResponse } from '../api/clients/api.models';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { loginTree } from '../routing/route-trees';
import type { SessionEndReason } from '../routing/route-paths';
import { APP_CLOCK } from '../session/clock';
import { MILLISECONDS_PER_MINUTE } from '../session/time-units';
import { CROSS_TAB_LOCK, CrossTabLockNames } from './cross-tab-lock';
import { TokenStore } from './token.store';

/** How long before the access token expires the browser refreshes it. */
export const ACCESS_TOKEN_REFRESH_LEAD_MS = MILLISECONDS_PER_MINUTE;

/**
 * The session lifecycle: login, proactive token refresh, logout and expiry.
 *
 * - Login stores the tokens and adopts the request-signing keys: the login response's `signing` block, else
 *   the block of `GET api/security/session-signing-key` (a 404 there means the server does not sign),
 *   and syncs the permissions. Its failures propagate unchanged (an `HttpErrorResponse` the login form
 *   parses with `parseApiError`, or an ng-security signing error).
 * - The access token is refreshed one minute before it expires; a failed refresh ends the session.
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
   * Signs in and loads the user's permissions.
   *
   * @throws the login failure; nothing is stored when it throws.
   */
  async login(userName: string, password: string): Promise<void> {
    const response = await firstValueFrom(this.client.login({ userName, password }));
    await this.adoptSession(response);
    try {
      await this.permissions.refresh();
    } catch {
      // The session is valid without the keys; the guards retry the sync before they decide.
    }
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

  /** Revokes the session on the server (best effort), clears it locally and goes to the login page. */
  async logout(): Promise<void> {
    if (this.isAuthenticated()) {
      try {
        await firstValueFrom(this.client.logout());
      } catch {
        // The server session may already be gone; the local session is cleared regardless.
      }
    }
    await this.endSession();
    await this.router.navigateByUrl(loginTree(this.router));
  }

  /** Ends a session the server or the idle timer gave up on, returning to the login page with a notice. */
  async expireSession(reason: SessionEndReason, returnUrl?: string): Promise<void> {
    await this.endSession();
    await this.router.navigateByUrl(loginTree(this.router, returnUrl, reason));
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
    let response: SessionResponse;
    try {
      response = await firstValueFrom(this.client.refresh(refreshToken));
    } catch {
      await this.endSession();
      return false;
    }
    await this.adoptSession(response);
    return true;
  }

  private async adoptSession(response: SessionResponse): Promise<void> {
    // The tokens come first: the signing-key request is authenticated with the new access token.
    this.tokens.save(response);
    try {
      await this.adoptSigningKeys(response);
    } catch (error) {
      this.tokens.clear();
      throw error;
    }
    this.scheduleRefresh();
  }

  private async adoptSigningKeys(response: SessionResponse): Promise<void> {
    if (!this.signingKeys) return;
    const block = response.signing ?? (await this.fetchSigningBlock());
    if (block) await this.signingKeys.adopt(block);
  }

  /** The session's signing block, or null when the server does not sign (404). */
  private async fetchSigningBlock(): Promise<SigningSessionBlock | null> {
    try {
      return await firstValueFrom(this.client.signingKey());
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
