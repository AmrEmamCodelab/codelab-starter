import { describe, expect, it } from 'vitest';
import { WIZARD_TRANSLATION_KEYS } from './wizard-translation-keys';

const KEY_FORMAT = /^patterns\.wizard\.[a-z-]+$/;

describe('WizardTranslationKeys', () => {
  it('has unique keys under patterns.wizard.*', () => {
    expect(new Set(WIZARD_TRANSLATION_KEYS).size).toBe(WIZARD_TRANSLATION_KEYS.length);
    for (const key of WIZARD_TRANSLATION_KEYS) {
      expect(key).toMatch(KEY_FORMAT);
    }
  });
});
