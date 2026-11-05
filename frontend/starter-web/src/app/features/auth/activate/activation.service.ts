import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthClient } from '../../../core/api/clients/auth.client';
import { parseLoginError, rejectionFor, type LoginRejection } from '../../../core/auth/login-rejection';
import { LoginNotices, RoutePaths, RouteQueryParams } from '../../../core/routing/route-paths';
import { BrowserLocation, parseFragmentParams } from '../../../core/session/browser-location';
import { ACTIVATION_TICKET_PARAM, serverPasswordErrorKey } from './activation-form';

/**
 * The activation flow (`/activate#ticket=`): an imported or admin-created account sets its first password.
 *
 * {@link init} takes the ticket from the raw fragment and clears it from the address bar and history at once; the
 * ticket then lives only in this per-page service (never logged, stored or put in a signal). {@link activate} posts
 * `api/auth/activate` and, on 204, goes to the login page with the "password set" notice. A refused ticket (spent,
 * expired, superseded) is `IDENTITY_ACTIVATION_REJECTED`; a policy failure is a password field error. Provided per page.
 */
@Injectable()
export class ActivationService {
  private readonly client = inject(AuthClient);
  private readonly router = inject(Router);
  private readonly browser = inject(BrowserLocation);
  private ticket: string | null = null;
  private readonly ticketPresent = signal(false);
  private readonly pending = signal(false);
  private readonly lastFailure = signal<LoginRejection | null>(null);
  private readonly serverPasswordError = signal<string | null>(null);

  /** False when the link carried no ticket: the page shows "this link is not valid". */
  readonly hasTicket = this.ticketPresent.asReadonly();
  readonly busy = this.pending.asReadonly();
  readonly failure = this.lastFailure.asReadonly();
  /** The server's password field error (a policy stricter than the client's check). */
  readonly passwordError = this.serverPasswordError.asReadonly();

  /** Reads and clears the fragment. Call once, when the page opens. */
  init(): void {
    const ticket = parseFragmentParams(this.browser.fragment()).get(ACTIVATION_TICKET_PARAM)?.trim() ?? '';
    this.browser.clearFragment();
    this.ticket = ticket === '' ? null : ticket;
    this.ticketPresent.set(this.ticket !== null);
  }

  /** @returns true when the password was set and the user has been sent to the login page. */
  async activate(password: string): Promise<boolean> {
    if (this.ticket === null || this.pending()) {
      return false;
    }
    this.pending.set(true);
    this.lastFailure.set(null);
    this.serverPasswordError.set(null);
    try {
      await firstValueFrom(this.client.activate({ ticket: this.ticket, password }));
    } catch (error: unknown) {
      this.showRefusal(error);
      return false;
    } finally {
      this.pending.set(false);
    }
    this.ticket = null;
    await this.router.navigate([RoutePaths.Login], {
      queryParams: { [RouteQueryParams.Notice]: LoginNotices.Activated },
      replaceUrl: true,
    });
    return true;
  }

  private showRefusal(error: unknown): void {
    const apiError = parseLoginError(error);
    const fieldKey = serverPasswordErrorKey(apiError.fieldErrors);
    if (fieldKey !== null) {
      this.serverPasswordError.set(fieldKey);
      return;
    }
    this.lastFailure.set(rejectionFor(apiError));
  }
}
