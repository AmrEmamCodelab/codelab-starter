import { describe, expect, it } from 'vitest';
import { REQUESTER_DETAIL_TRANSLATION_KEYS } from './requester-detail-translation-keys';

const KEY_FORMAT = /^patterns\.requester-detail\.[a-z-]+$/;

describe('RequesterDetailTranslationKeys', () => {
  it('has unique keys under patterns.requester-detail.*', () => {
    expect(new Set(REQUESTER_DETAIL_TRANSLATION_KEYS).size).toBe(REQUESTER_DETAIL_TRANSLATION_KEYS.length);
    for (const key of REQUESTER_DETAIL_TRANSLATION_KEYS) {
      expect(key).toMatch(KEY_FORMAT);
    }
  });
});
