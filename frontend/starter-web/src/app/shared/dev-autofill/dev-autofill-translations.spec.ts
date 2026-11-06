import { describe, expect, it } from 'vitest';
import ar from '../../../assets/i18n/ar.json';
import en from '../../../assets/i18n/en.json';
import { flattenTranslations } from '../../core/i18n/translation.service';
import { DevAutofillTranslationKeys } from './dev-autofill-keys';

const ARABIC_LETTER = /[؀-ۿ]/;
const KEYS = Object.values(DevAutofillTranslationKeys);

describe('dev autofill translations', () => {
  const bundles = { ar: flattenTranslations(ar), en: flattenTranslations(en) };

  it.each(['ar', 'en'] as const)('has every key in the %s bundle', (language) => {
    expect(KEYS.filter((key) => !bundles[language][key])).toEqual([]);
  });

  it('uses Arabic letters in every Arabic text', () => {
    for (const key of KEYS) expect(bundles.ar[key], key).toMatch(ARABIC_LETTER);
  });
});
