import { describe, expect, it } from 'vitest';
import ar from '../../../assets/i18n/ar.json';
import en from '../../../assets/i18n/en.json';
import { flattenTranslations } from '../../core/i18n/translation.service';
import { PORTAL_TRANSLATION_KEYS } from './portal-translation-keys';

const KEY_FORMAT = /^portal\.[a-z-]+\.[a-z0-9-]+$/;
const PLACEHOLDER = /\{\{\s*([\w.-]+)\s*\}\}|\{(count|max|remaining)\}/g;
const ARABIC_LETTER = /[؀-ۿ]/;

function placeholders(text: string | undefined): string[] {
  return [...(text ?? '').matchAll(PLACEHOLDER)].map(match => match[1] ?? match[2] ?? '').sort();
}

describe('portal translations', () => {
  const bundles = { ar: flattenTranslations(ar), en: flattenTranslations(en) };

  it('has unique keys under portal.<page>.<name>', () => {
    expect(new Set(PORTAL_TRANSLATION_KEYS).size).toBe(PORTAL_TRANSLATION_KEYS.length);
    for (const key of PORTAL_TRANSLATION_KEYS) {
      expect(key).toMatch(KEY_FORMAT);
    }
  });

  it.each(['ar', 'en'] as const)('has every portal key in the %s bundle', (language) => {
    expect(PORTAL_TRANSLATION_KEYS.filter(key => !bundles[language][key])).toEqual([]);
  });

  it('holds no portal key the feature does not render', () => {
    for (const bundle of [bundles.ar, bundles.en]) {
      const extra = Object.keys(bundle).filter(key => key.startsWith('portal.') && !PORTAL_TRANSLATION_KEYS.includes(key));
      expect(extra).toEqual([]);
    }
  });

  it('uses the same placeholders in Arabic and English, and Arabic letters in every Arabic text', () => {
    for (const key of PORTAL_TRANSLATION_KEYS) {
      expect(placeholders(bundles.ar[key]), key).toEqual(placeholders(bundles.en[key]));
      expect(bundles.ar[key], key).toMatch(ARABIC_LETTER);
    }
  });
});
