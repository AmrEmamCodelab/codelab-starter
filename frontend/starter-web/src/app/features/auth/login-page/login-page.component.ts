import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { DgaButtonComponent, DgaCardComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { RouteQueryParams } from '../../../core/routing/route-paths';
import { TranslationService } from '../../../core/i18n/translation.service';
import { LanguageSwitcherComponent } from '../../../layouts/language-switcher/language-switcher.component';
import { credentialErrors, hasCredentialErrors, sessionEndedNoticeKey, type Credentials } from '../login-form';
import { SignInService } from '../sign-in.service';

/**
 * The basic sign-in page: user name and password. Field checks show after the first submit; a refusal
 * from the server shows inline from its problem+json code. `?returnUrl=` and `?reason=` come from the query
 * string.
 */
@Component({
  selector: 'app-login-page',
  imports: [DgaButtonComponent, DgaCardComponent, DgaTextInputComponent, LanguageSwitcherComponent, TranslatePipe],
  providers: [SignInService],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPageComponent {
  private readonly translations = inject(TranslationService);
  readonly signIn = inject(SignInService);

  private readonly query = toSignal(inject(ActivatedRoute).queryParamMap);

  /** Where to go after signing in; only an app-relative URL is honoured. */
  readonly returnUrl = computed(() => this.query()?.get(RouteQueryParams.ReturnUrl) ?? null);
  /** Why the previous session ended. */
  readonly reason = computed(() => this.query()?.get(RouteQueryParams.Reason) ?? null);

  readonly userNameInput = signal('');
  readonly passwordInput = signal('');
  readonly submitted = signal(false);

  readonly noticeKey = computed(() => sessionEndedNoticeKey(this.reason()));
  private readonly errors = computed(() =>
    credentialErrors({ userName: this.userNameInput(), password: this.passwordInput() }),
  );
  readonly userNameError = computed(() => this.shownError(this.errors().userName));
  readonly passwordError = computed(() => this.shownError(this.errors().password));

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (hasCredentialErrors(this.errors())) {
      return;
    }
    const credentials: Credentials = { userName: this.userNameInput(), password: this.passwordInput() };
    await this.signIn.signIn(credentials, this.returnUrl());
  }

  private shownError(key: string | null): string | null {
    return this.submitted() && key !== null ? this.translations.translate(key) : null;
  }
}
