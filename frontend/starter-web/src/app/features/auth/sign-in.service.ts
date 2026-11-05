import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { LoginMessageKeys, loginRejectionFrom, type LoginRejection } from '../../core/auth/login-rejection';
import { SignInMode } from '../../core/auth/sign-in-mode';
import { RoutePaths } from '../../core/routing/route-paths';
import { safeReturnUrl } from '../../core/routing/route-trees';
import type { Credentials } from './login-form';

/** What the login page shows after a failed sign-in. */
export type SignInFailure = LoginRejection;

/** Shown when the browser refused to leave for the SSO sign-in (it does not navigate to a malformed URL). */
export const SsoStartFailure: SignInFailure = { messageKey: 'auth.sso.start-failed', referenceCode: null };

/** Every translation key the sign-in flow shows on its own (the rest are `errors.<CODE>`). */
export const SignInMessageKeys = {
  ...LoginMessageKeys,
  SsoStartFailed: SsoStartFailure.messageKey,
} as const;

/**
 * The login page's flow: signs in through {@link AuthService} with the chosen method, then goes to the safe return
 * URL (or the dashboard). A refusal becomes a {@link SignInFailure} the page shows inline (the core
 * `loginRejectionFrom()` mapper; a lockout reads as the generic invalid-credentials sentence); the calls are
 * self-handled, so the app-wide error dialog stays closed. Provided per page.
 */
@Injectable()
export class SignInService {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly pending = signal(false);
  private readonly lastFailure = signal<SignInFailure | null>(null);

  readonly busy = this.pending.asReadonly();
  readonly failure = this.lastFailure.asReadonly();

  /**
   * Signs in with a user name and password, against the basic or the Active Directory endpoint.
   *
   * @returns true when the user is signed in and has been sent on.
   */
  async signIn(credentials: Credentials, method: SignInMode, returnUrl: string | null): Promise<boolean> {
    if (this.pending()) {
      return false;
    }
    this.pending.set(true);
    this.lastFailure.set(null);
    try {
      await this.passwordSignIn(credentials, method);
    } catch (error: unknown) {
      this.lastFailure.set(loginRejectionFrom(error));
      return false;
    } finally {
      this.pending.set(false);
    }
    const target = safeReturnUrl(returnUrl) ?? this.router.createUrlTree([RoutePaths.Dashboard]);
    await this.router.navigateByUrl(target);
    return true;
  }

  /** Leaves for the SSO sign-in; the page stays busy until the browser navigates away. */
  async startSso(returnUrl: string | null): Promise<void> {
    if (this.pending()) {
      return;
    }
    this.lastFailure.set(null);
    this.pending.set(true);
    if (await this.auth.startSsoSignIn(returnUrl)) {
      return;
    }
    this.pending.set(false);
    this.lastFailure.set(SsoStartFailure);
  }

  /** Forgets the last refusal (the user switched to another method). */
  clearFailure(): void {
    this.lastFailure.set(null);
  }

  private passwordSignIn(credentials: Credentials, method: SignInMode): Promise<void> {
    const userName = credentials.userName.trim();
    return method === SignInMode.ActiveDirectory
      ? this.auth.loginWithDirectory(userName, credentials.password)
      : this.auth.login(userName, credentials.password);
  }
}
