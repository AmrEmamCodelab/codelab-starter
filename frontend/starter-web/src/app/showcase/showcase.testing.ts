import { signal, type Provider, type WritableSignal } from '@angular/core';
import { provideRouter } from '@angular/router';
import ar from '../../assets/i18n/ar.json';
import en from '../../assets/i18n/en.json';
import { AuthService } from '../core/auth/auth.service';
import type { AppConfig } from '../core/config/app-config';
import { TEST_APP_CONFIG, provideTestAppConfig } from '../core/config/app-config.testing';
import { AppLanguages, directionOf, type AppLanguage } from '../core/i18n/language';
import { LanguageService } from '../core/i18n/language.service';
import {
  TranslationService,
  flattenTranslations,
  interpolate,
  type TranslationParams,
} from '../core/i18n/translation.service';
import { provideShowcaseClock } from './showcase-clock';

/**
 * Test doubles for the showcase specs (spec-only; nothing in the app imports this file). Translations come from the
 * real ar/en bundles, so a spec can assert the text a reader sees and a missing key shows up as the raw key.
 */
export const SHOWCASE_TEST_CONFIG: AppConfig = { ...TEST_APP_CONFIG, showcase: true };

const BUNDLES: Readonly<Record<AppLanguage, Readonly<Record<string, string>>>> = {
  [AppLanguages.Arabic]: flattenTranslations(ar),
  [AppLanguages.English]: flattenTranslations(en),
};

/** The text a key has in a language's bundle (the key itself when the bundle lacks it). */
export function bundleText(language: AppLanguage, key: string, params?: TranslationParams): string {
  return interpolate(BUNDLES[language][key] ?? key, params);
}

/** A language service and a translation service that read the real bundles for the current language. */
export interface ShowcaseTestLanguage {
  readonly current: WritableSignal<AppLanguage>;
  readonly providers: Provider[];
}

export function showcaseTestLanguage(language: AppLanguage = AppLanguages.Arabic): ShowcaseTestLanguage {
  const current = signal(language);
  const setLanguage = (next: AppLanguage) => {
    current.set(next);
    document.documentElement.lang = next;
    document.documentElement.dir = directionOf(next);
    return Promise.resolve();
  };
  void setLanguage(language);
  return {
    current,
    providers: [
      { provide: LanguageService, useValue: { current, set: setLanguage } },
      {
        provide: TranslationService,
        useValue: {
          language: current.asReadonly(),
          translate: (key: string, params?: TranslationParams) => bundleText(current(), key, params),
        },
      },
    ],
  };
}

/** Everything a showcase page needs in a spec: config with the flag on, the bundles, the clock, a signed-out session. */
export function showcaseTestProviders(language: ShowcaseTestLanguage, config: AppConfig = SHOWCASE_TEST_CONFIG): Provider[] {
  return [
    provideTestAppConfig(config),
    provideRouter([]),
    provideShowcaseClock(),
    ...language.providers,
    {
      provide: AuthService,
      useValue: { user: signal(null), isAuthenticated: signal(false), logout: () => Promise.resolve() },
    },
  ];
}
