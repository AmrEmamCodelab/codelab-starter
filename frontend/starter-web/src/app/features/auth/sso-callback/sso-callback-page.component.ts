import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { DgaButtonComponent, DgaCardComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { SsoCallbackService, SsoCallbackStates } from './sso-callback.service';

/** Translation keys of the SSO callback page. */
export const SsoCallbackTextKeys = {
  Title: 'auth.sso.title',
  Completing: 'auth.sso.completing',
  FailedTitle: 'auth.sso.failed-title',
  BackToLogin: 'auth.sso.back-to-login',
} as const;

/**
 * `/auth/sso/callback` (public): shows progress while {@link SsoCallbackService} swaps the one-time ticket, and the
 * refusal with a way back to the login page when it fails.
 */
@Component({
  selector: 'app-sso-callback-page',
  imports: [DgaButtonComponent, DgaCardComponent, TranslatePipe],
  providers: [SsoCallbackService],
  templateUrl: './sso-callback-page.component.html',
  styleUrl: './sso-callback-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SsoCallbackPageComponent {
  readonly callback = inject(SsoCallbackService);
  readonly states = SsoCallbackStates;
  readonly texts = SsoCallbackTextKeys;

  constructor() {
    void this.callback.complete();
  }

  backToLogin(): void {
    void this.callback.backToLogin();
  }
}
