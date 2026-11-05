import { describe, expect, it } from 'vitest';
import ar from '../../assets/i18n/ar.json';
import en from '../../assets/i18n/en.json';
import { flattenTranslations } from '../core/i18n/translation.service';
import { SHOWCASE_TRANSLATION_KEYS } from './showcase-translation-keys';

const KEY_FORMAT = /^showcase\.[a-z-]+\.[a-z-]+$/;
const ARABIC_LETTER = /\p{Script=Arabic}/u;
const PLACEHOLDER = /\{\{?\s*[\w.-]+\s*\}?\}/g;

const flatAr = flattenTranslations(ar);
const flatEn = flattenTranslations(en);

describe('ShowcaseTranslationKeys', () => {
  it('has unique keys, all under showcase.<area>.<name>', () => {
    expect(new Set(SHOWCASE_TRANSLATION_KEYS).size).toBe(SHOWCASE_TRANSLATION_KEYS.length);
    for (const key of SHOWCASE_TRANSLATION_KEYS) {
      expect(key).toMatch(KEY_FORMAT);
    }
  });

  it.each([
    ['ar', flatAr],
    ['en', flatEn],
  ])('has every key in the %s bundle', (_language, flat) => {
    for (const key of SHOWCASE_TRANSLATION_KEYS) {
      expect(flat[key], key).toBeTruthy();
    }
  });

  it('has Arabic text in Arabic and the same placeholders in both languages', () => {
    for (const key of SHOWCASE_TRANSLATION_KEYS) {
      expect(flatAr[key], key).toMatch(ARABIC_LETTER);
      expect((flatAr[key] ?? '').match(PLACEHOLDER)?.sort(), key).toEqual((flatEn[key] ?? '').match(PLACEHOLDER)?.sort());
    }
  });

  it('has no showcase key in the bundles that the catalogue does not list', () => {
    const listed = new Set<string>(SHOWCASE_TRANSLATION_KEYS);
    const extra = Object.keys(flatEn).filter(key => key.startsWith('showcase.') && !listed.has(key));
    expect(extra).toEqual([]);
  });
});
