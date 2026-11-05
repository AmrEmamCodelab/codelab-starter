import { describe, expect, it } from 'vitest';
import { DECISION_DIALOG_TRANSLATION_KEYS, DecisionDialogTranslationKeys } from './decision-dialog-translation-keys';
import {
  DECISION_REASON_MAX_LENGTH,
  DECISION_REASON_MIN_LENGTH,
  DEFAULT_REASON_LIMITS,
  ReasonProblem,
  normalizeReason,
  reasonProblem,
  reasonProblemMessage,
} from './decision-reason';

describe('normalizeReason', () => {
  it('trims and treats null as empty', () => {
    expect(normalizeReason('  late documents \n')).toBe('late documents');
    expect(normalizeReason(null)).toBe('');
    expect(normalizeReason(undefined)).toBe('');
  });
});

describe('reasonProblem', () => {
  it('requires a reason; blank counts as missing', () => {
    expect(reasonProblem('')).toBe(ReasonProblem.Required);
    expect(reasonProblem('   ')).toBe(ReasonProblem.Required);
    expect(reasonProblem(null)).toBe(ReasonProblem.Required);
  });

  it('checks the default limits on the trimmed text', () => {
    expect(DEFAULT_REASON_LIMITS).toEqual({ minLength: DECISION_REASON_MIN_LENGTH, maxLength: DECISION_REASON_MAX_LENGTH });
    expect(reasonProblem('a'.repeat(DECISION_REASON_MIN_LENGTH - 1))).toBe(ReasonProblem.TooShort);
    expect(reasonProblem(`  ${'a'.repeat(DECISION_REASON_MIN_LENGTH - 1)}  `)).toBe(ReasonProblem.TooShort);
    expect(reasonProblem('a'.repeat(DECISION_REASON_MIN_LENGTH))).toBeNull();
    expect(reasonProblem('a'.repeat(DECISION_REASON_MAX_LENGTH))).toBeNull();
    expect(reasonProblem('a'.repeat(DECISION_REASON_MAX_LENGTH + 1))).toBe(ReasonProblem.TooLong);
  });

  it('honours screen limits', () => {
    const limits = { minLength: 2, maxLength: 4 };
    expect(reasonProblem('ab', limits)).toBeNull();
    expect(reasonProblem('a', limits)).toBe(ReasonProblem.TooShort);
    expect(reasonProblem('abcde', limits)).toBe(ReasonProblem.TooLong);
  });

  it('accepts Arabic text by its character count', () => {
    expect(reasonProblem('مستندات ناقصة')).toBeNull();
  });
});

describe('reasonProblemMessage', () => {
  const limits = { minLength: 3, maxLength: 9 };

  it('gives the key and the limit it names', () => {
    expect(reasonProblemMessage(ReasonProblem.Required, limits)).toEqual({ key: DecisionDialogTranslationKeys.ReasonRequired });
    expect(reasonProblemMessage(ReasonProblem.TooShort, limits)).toEqual({
      key: DecisionDialogTranslationKeys.ReasonTooShort,
      params: { min: 3 },
    });
    expect(reasonProblemMessage(ReasonProblem.TooLong, limits)).toEqual({
      key: DecisionDialogTranslationKeys.ReasonTooLong,
      params: { max: 9 },
    });
  });
});

describe('DecisionDialogTranslationKeys', () => {
  it('has unique keys under patterns.decision-dialog.*', () => {
    expect(new Set(DECISION_DIALOG_TRANSLATION_KEYS).size).toBe(DECISION_DIALOG_TRANSLATION_KEYS.length);
    for (const key of DECISION_DIALOG_TRANSLATION_KEYS) {
      expect(key).toMatch(/^patterns\.decision-dialog\.[a-z-]+$/);
    }
  });
});
