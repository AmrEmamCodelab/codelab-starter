import { computed, inject, Injectable } from '@angular/core';
import type { DgaBranding } from '@codelab/ng-dga';
import { AppConfigService } from '../../core/config/app-config.service';
import type { AppLanguage } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { TranslationService } from '../../core/i18n/translation.service';
import { LayoutChromeTranslationKeys } from '../portal-shell/layout-chrome-translation-keys';

/** The product name in `language` from the branding block, or null without one (the caller falls back to i18n). */
export function brandNameFor(branding: DgaBranding | undefined, language: AppLanguage): string | null {
  return branding?.appName[language] ?? null;
}

/** The logo path from the branding block, or null when there is none or it is empty (the name shows alone). */
export function brandLogoFor(branding: DgaBranding | undefined): string | null {
  const logo = branding?.logoSrc ?? '';
  return logo.length > 0 ? logo : null;
}

/**
 * The project's name and logo as the layouts show them (header brand, footer). Both come from the `branding` block of
 * `config.json` (validated at bootstrap by @codelab/ng-dga); without one, the name is the `layouts.brand.name`
 * translation and there is no logo. The name follows the current language.
 */
@Injectable({ providedIn: 'root' })
export class LayoutBrand {
  private readonly branding = inject(AppConfigService).branding;
  private readonly language = inject(LanguageService);
  private readonly translations = inject(TranslationService);

  readonly name = computed(
    () =>
      brandNameFor(this.branding, this.language.current()) ??
      this.translations.translate(LayoutChromeTranslationKeys.BrandName),
  );
  readonly logoSrc = brandLogoFor(this.branding);
}
