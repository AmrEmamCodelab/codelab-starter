import { signal, type EnvironmentProviders, type Provider } from '@angular/core';
import { provideRouter } from '@angular/router';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';
import { TranslationService, type TranslationParams } from '../../core/i18n/translation.service';
import { echoTranslate } from '../../patterns/patterns.testing';

/**
 * Test doubles for the portal specs (spec-only; nothing in the app imports this file). Translations echo their key
 * (and `key|name=value` for parameters), unless the spec gives texts for some keys.
 */
export class FakePortalTranslations {
  readonly language = signal<AppLanguage>(AppLanguages.Arabic);
  private readonly texts = new Map<string, string>();

  /** Gives `key` a real text (search specs match on it); later calls win. */
  define(entries: Readonly<Record<string, string>>): void {
    for (const [key, text] of Object.entries(entries)) {
      this.texts.set(key, text);
    }
  }

  translate(key: string, params?: TranslationParams): string {
    const text = this.texts.get(key);
    return text === undefined ? echoTranslate(key, params) : text;
  }

  /** Only `portal.*` keys and keys given through {@link define} count as translated. */
  has(key: string): boolean {
    return this.texts.has(key) || key.startsWith('portal.');
  }
}

/** A fresh fake, its provider and the router every portal page needs. */
export function portalTestProviders(translations = new FakePortalTranslations()): (Provider | EnvironmentProviders)[] {
  return [{ provide: TranslationService, useValue: translations }, provideRouter([])];
}

/** The text directions a page spec renders in (Arabic first). */
export const TEST_DIRECTIONS: readonly ['rtl' | 'ltr', AppLanguage][] = [
  ['rtl', AppLanguages.Arabic],
  ['ltr', AppLanguages.English],
];
