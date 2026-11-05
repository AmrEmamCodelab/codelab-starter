import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ssoErrorRejection, ssoExchangeRejectionFrom, type LoginRejection } from '../../../core/auth/login-rejection';
import { parseSsoCallbackFragment } from '../../../core/auth/sso';
import { RoutePaths } from '../../../core/routing/route-paths';
import { loginTree, safeReturnUrl } from '../../../core/routing/route-trees';
import { BrowserLocation } from '../../../core/session/browser-location';

/** Where the callback stands. */
export const SsoCallbackStates = {
  Working: 'working',
  Failed: 'failed',
} as const;

export type SsoCallbackState = (typeof SsoCallbackStates)[keyof typeof SsoCallbackStates];

/**
 * The SSO callback flow (`/auth/sso/callback`), where the API sends the browser back from the identity provider.
 *
 * 1. Reads `#ticket=` (or `#error=`) from the raw fragment and clears it from the address bar and the history entry
 *    BEFORE anything else, success or not: the ticket must not outlive this read on screen, in Back or in history.
 * 2. Swaps the ticket once (`AuthService.completeSsoSignIn`) with the browser-binding secret THIS tab kept at the SSO
 *    start; a tab that started no sign-in (a copied ticket link) refuses without calling the API; a second {@link complete} on the same page does nothing,
 *    and a revisit finds no ticket. A replayed or expired ticket is refused by the server and shown as such.
 * 3. Lands on the safe return URL (the fragment's, else the one remembered at the SSO start, else the dashboard),
 *    replacing this history entry so Back never re-runs a spent callback.
 *
 * The ticket is never logged, stored or put in a signal. Provided per page.
 */
@Injectable()
export class SsoCallbackService {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly browser = inject(BrowserLocation);
  private readonly current = signal<SsoCallbackState>(SsoCallbackStates.Working);
  private readonly lastFailure = signal<LoginRejection | null>(null);
  private started = false;
  private returnUrl: string | null = null;

  readonly state = this.current.asReadonly();
  readonly failure = this.lastFailure.asReadonly();

  /** Runs the callback once. Never throws: a refusal becomes {@link failure}. */
  async complete(): Promise<void> {
    if (this.started) {
      return;
    }
    this.started = true;
    const fragment = parseSsoCallbackFragment(this.browser.fragment());
    this.browser.clearFragment();
    const remembered = this.auth.takeSsoReturnUrl();
    this.returnUrl = safeReturnUrl(fragment.returnUrl) ?? remembered;
    const proof = this.auth.takeSsoBindingProof();
    if (fragment.ticket === null) {
      this.fail(ssoErrorRejection(fragment.error));
      return;
    }
    if (proof === null) {
      this.fail(ssoErrorRejection(null));
      return;
    }
    try {
      await this.auth.completeSsoSignIn(fragment.ticket, proof);
    } catch (error: unknown) {
      this.fail(ssoExchangeRejectionFrom(error));
      return;
    }
    const target = this.returnUrl ?? this.router.createUrlTree([RoutePaths.Dashboard]);
    await this.router.navigateByUrl(target, { replaceUrl: true });
  }

  /** Back to the login page, keeping the deep link the sign-in started from. */
  async backToLogin(): Promise<void> {
    await this.router.navigateByUrl(loginTree(this.router, this.returnUrl ?? undefined), { replaceUrl: true });
  }

  private fail(rejection: LoginRejection): void {
    this.lastFailure.set(rejection);
    this.current.set(SsoCallbackStates.Failed);
  }
}
