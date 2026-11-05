import { describe, expect, it } from 'vitest';
import { STATUS_TRACKER_TRANSLATION_KEYS } from './status-tracker-translation-keys';

const KEY_FORMAT = /^patterns\.status-tracker\.[a-z-]+$/;

describe('StatusTrackerTranslationKeys', () => {
  it('has unique keys under patterns.status-tracker.*', () => {
    expect(new Set(STATUS_TRACKER_TRANSLATION_KEYS).size).toBe(STATUS_TRACKER_TRANSLATION_KEYS.length);
    for (const key of STATUS_TRACKER_TRANSLATION_KEYS) {
      expect(key).toMatch(KEY_FORMAT);
    }
  });
});
