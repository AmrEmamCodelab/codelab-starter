import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DgaButtonComponent } from '@codelab/ng-dga';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/** The translation key of each language's own name, shown on the button that switches to it. */
export const LanguageNameKeys: Readonly<Record<AppLanguage, string>> = {
  [AppLanguages.Arabic]: 'layout.language-switcher.ar',
  [AppLanguages.English]: 'layout.language-switcher.en',
};

/** The language the switcher offers: the other of the two the app ships. */
export function otherLanguage(current: AppLanguage): AppLanguage {
  return current === AppLanguages.Arabic ? AppLanguages.English : AppLanguages.Arabic;
}

/**
 * Switches between Arabic and English through {@link LanguageService}, which sets `<html lang dir>`;
 * the layout mirrors through logical CSS. The button shows the target language in its own script.
 */
@Component({
  selector: 'app-language-switcher',
  imports: [DgaButtonComponent, TranslatePipe],
  templateUrl: './language-switcher.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LanguageSwitcherComponent {
  private readonly language = inject(LanguageService);

  readonly target = computed(() => otherLanguage(this.language.current()));
  readonly targetNameKey = computed(() => LanguageNameKeys[this.target()]);

  switch(): Promise<void> {
    return this.language.set(this.target());
  }
}
