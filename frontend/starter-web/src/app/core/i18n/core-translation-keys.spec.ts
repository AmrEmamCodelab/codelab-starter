import { ApiErrorCodes, apiErrorMessageKey, parseApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { SessionEndReasons } from '../routing/route-paths';
import { CORE_TRANSLATION_KEYS, CoreTranslationKeys } from './core-translation-keys';

const KEY_FORMAT = /^[a-z][a-z0-9-]*(\.[a-zA-Z0-9_-]+)+$/;

describe('CoreTranslationKeys', () => {
  it('lists a message key for every platform error code', () => {
    for (const code of Object.values(ApiErrorCodes)) {
      expect(CORE_TRANSLATION_KEYS).toContain(apiErrorMessageKey(parseApiError({ status: 0, error: { code } })));
    }
  });

  it('lists a notice for every session end reason', () => {
    for (const reason of Object.values(SessionEndReasons)) {
      expect(CORE_TRANSLATION_KEYS).toContain(CoreTranslationKeys.SessionEnded[reason]);
    }
  });

  it('has unique, well-formed keys', () => {
    expect(new Set(CORE_TRANSLATION_KEYS).size).toBe(CORE_TRANSLATION_KEYS.length);
    for (const key of CORE_TRANSLATION_KEYS) {
      expect(key).toMatch(KEY_FORMAT);
    }
  });
});
