import type { Provider } from '@angular/core';
import { TranslationService, type TranslationParams } from '../core/i18n/translation.service';

/**
 * Test doubles for the pattern specs (spec-only; nothing in the app imports this file). Translations echo their key,
 * and `key|name=value` for parameters, so assertions read the key a template asked for.
 */
export function echoTranslate(key: string, params?: TranslationParams): string {
  if (!params) {
    return key;
  }
  return `${key}|${Object.entries(params).map(([name, value]) => `${name}=${value}`).join(',')}`;
}

export function patternTestProviders(): Provider[] {
  return [{ provide: TranslationService, useValue: { translate: echoTranslate } }];
}

/** The trimmed text of the first element matching `selector`, or null. */
export function textOf(root: ParentNode, selector: string): string | null {
  return root.querySelector(selector)?.textContent?.trim() ?? null;
}
