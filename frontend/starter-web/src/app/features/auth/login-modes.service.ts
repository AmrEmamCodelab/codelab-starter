import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AuthClient } from '../../core/api/clients/auth.client';
import { loginRejectionFrom, type LoginRejection } from '../../core/auth/login-rejection';
import type { SignInMode } from '../../core/auth/sign-in-mode';
import { loginMethodsFrom } from './login-form';

/** Where loading the enabled sign-in methods stands. */
export const LoginModesStates = {
  Loading: 'loading',
  Ready: 'ready',
  Failed: 'failed',
} as const;

export type LoginModesState = (typeof LoginModesStates)[keyof typeof LoginModesStates];

/**
 * The sign-in methods this deployment enables (`GET api/auth/modes`, the API's `Auth:Modes`), loaded once per visit
 * of the login page. The page shows only these; a method the server switched off answers 404 anyway. A failed load is
 * shown with a retry rather than guessed, so an SSO-only deployment never shows a password form. Provided per page.
 */
@Injectable()
export class LoginModesService {
  private readonly client = inject(AuthClient);
  private readonly current = signal<LoginModesState>(LoginModesStates.Loading);
  private readonly enabled = signal<readonly SignInMode[]>([]);
  private readonly lastFailure = signal<LoginRejection | null>(null);

  readonly state = this.current.asReadonly();
  /** The enabled methods in display order; empty until loaded. */
  readonly methods = this.enabled.asReadonly();
  /** Why the methods could not be loaded. */
  readonly failure = this.lastFailure.asReadonly();
  /** True when more than one method is enabled, so the page shows a chooser. */
  readonly hasChoice = computed(() => this.enabled().length > 1);

  /** Loads (or reloads) the enabled methods. Never throws. */
  async load(): Promise<void> {
    this.current.set(LoginModesStates.Loading);
    this.lastFailure.set(null);
    try {
      this.enabled.set(loginMethodsFrom(await firstValueFrom(this.client.modes())));
      this.current.set(LoginModesStates.Ready);
    } catch (error: unknown) {
      this.enabled.set([]);
      this.lastFailure.set(loginRejectionFrom(error));
      this.current.set(LoginModesStates.Failed);
    }
  }
}
