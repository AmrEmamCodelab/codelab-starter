import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DgaButtonComponent, DgaCardComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { TranslationService } from '../../../core/i18n/translation.service';
import { RoutePaths } from '../../../core/routing/route-paths';
import { LanguageSwitcherComponent } from '../../../layouts/language-switcher/language-switcher.component';
import {
  ACTIVATION_PASSWORD_MIN_LENGTH,
  ActivationMessageKeys,
  hasNewPasswordErrors,
  newPasswordErrors,
} from './activation-form';
import { ActivationService } from './activation.service';

/**
 * `/activate#ticket=` (public): the first password of an account, from the activation link an administrator or the
 * user import issued. Field checks show after the first submit; the server's refusal shows inline.
 */
@Component({
  selector: 'app-activate-page',
  imports: [DgaButtonComponent, DgaCardComponent, DgaTextInputComponent, LanguageSwitcherComponent, TranslatePipe],
  providers: [ActivationService],
  templateUrl: './activate-page.component.html',
  styleUrl: './activate-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ActivatePageComponent {
  private readonly translations = inject(TranslationService);
  private readonly router = inject(Router);
  readonly activation = inject(ActivationService);
  readonly texts = ActivationMessageKeys;
  readonly minLength = ACTIVATION_PASSWORD_MIN_LENGTH;

  readonly passwordInput = signal('');
  readonly confirmInput = signal('');
  readonly submitted = signal(false);

  private readonly errors = computed(() =>
    newPasswordErrors({ password: this.passwordInput(), confirm: this.confirmInput() }),
  );
  readonly passwordError = computed(() =>
    this.shownError(this.errors().password) ?? this.translated(this.activation.passwordError()),
  );
  readonly confirmError = computed(() => this.shownError(this.errors().confirm));

  constructor() {
    this.activation.init();
  }

  async submit(event: Event): Promise<void> {
    event.preventDefault();
    this.submitted.set(true);
    if (hasNewPasswordErrors(this.errors())) {
      return;
    }
    await this.activation.activate(this.passwordInput());
  }

  backToLogin(): void {
    void this.router.navigate([RoutePaths.Login]);
  }

  private shownError(key: string | null): string | null {
    return this.submitted() ? this.translated(key) : null;
  }

  private translated(key: string | null): string | null {
    return key === null ? null : this.translations.translate(key, { min: ACTIVATION_PASSWORD_MIN_LENGTH });
  }
}
