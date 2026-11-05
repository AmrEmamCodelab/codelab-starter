import { describe, expect, it } from 'vitest';
import ar from '../../assets/i18n/ar.json';
import en from '../../assets/i18n/en.json';
import { flattenTranslations } from '../core/i18n/translation.service';
import { PATTERN_TRANSLATION_KEYS } from './pattern-translation-keys';

const KEY_FORMAT = /^patterns\.[a-z-]+\.[a-z-]+$/;

describe('PatternTranslationKeys', () => {
  it('has unique keys under patterns.*', () => {
    expect(new Set(PATTERN_TRANSLATION_KEYS).size).toBe(PATTERN_TRANSLATION_KEYS.length);
    for (const key of PATTERN_TRANSLATION_KEYS) {
      expect(key).toMatch(KEY_FORMAT);
    }
  });

  it.each([['ar', ar], ['en', en]])('has every key in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    for (const key of PATTERN_TRANSLATION_KEYS) {
      expect(flat[key], key).toBeTruthy();
    }
  });
});
