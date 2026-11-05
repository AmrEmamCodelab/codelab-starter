import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HttpStatus, apiErrorMessageKey, parseApiError, type ApiError } from '@codelab/ng-security';
import { AuthService } from '../../core/auth/auth.service';
import { RoutePaths } from '../../core/routing/route-paths';
import { safeReturnUrl } from '../../core/routing/route-trees';
import type { Credentials } from './login-form';

/**
 * The problem+json codes the login endpoint answers with on its own. They mirror the backend's
 * `IdentityErrorCodes` (backend/src/Modules/Identity/Application/IdentityErrorCodes.cs); the contract is the
 * code, never the HTTP status (the server answers a wrong password with 400).
 */
export const SignInErrorCodes = {
  /** The user name and password do not match an active account. One code for every reason. */
  InvalidCredentials: 'IDENTITY_INVALID_CREDENTIALS',
} as const;

/** Translation keys the sign-in flow shows on its own (the rest come from the platform error codes). */
export const SignInMessageKeys = {
  /** The server refused the user name and password. It never says which of the two was wrong. */
  InvalidCredentials: 'auth.login.invalid-credentials',
} as const;

/** What the login page shows after a failed sign-in. */
export interface SignInFailure {
  readonly messageKey: string;
  /** The short code the user reads out to support; present for outages and server errors. */
  readonly referenceCode: string | null;
}

/** An unexpected failure that is not an HTTP response (for example a request-signing error). */
const NON_HTTP_FAILURE = { status: HttpStatus.serverError } as const;

/** The failure to show for whatever `AuthService.login` threw, read from its problem+json body. */
export function signInFailureFrom(error: unknown): SignInFailure {
  const apiError: ApiError = parseApiError(error instanceof HttpErrorResponse ? error : NON_HTTP_FAILURE);
  if (apiError.serverCode === SignInErrorCodes.InvalidCredentials) {
    // A user mistake, not a fault: nothing for support to trace, so no reference code.
    return { messageKey: SignInMessageKeys.InvalidCredentials, referenceCode: null };
  }
  return { messageKey: apiErrorMessageKey(apiError), referenceCode: apiError.referenceCode ?? null };
}

/**
 * The login page's flow: signs in through {@link AuthService}, then goes to the safe return URL (or the
 * dashboard). A refusal becomes a {@link SignInFailure} the page shows inline; the login call is
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

  /** @returns true when the user is signed in and has been sent on. */
  async signIn(credentials: Credentials, returnUrl: string | null): Promise<boolean> {
    if (this.pending()) {
      return false;
    }
    this.pending.set(true);
    this.lastFailure.set(null);
    try {
      await this.auth.login(credentials.userName.trim(), credentials.password);
    } catch (error: unknown) {
      this.lastFailure.set(signInFailureFrom(error));
      return false;
    } finally {
      this.pending.set(false);
    }
    const target = safeReturnUrl(returnUrl) ?? this.router.createUrlTree([RoutePaths.Dashboard]);
    await this.router.navigateByUrl(target);
    return true;
  }
}
