import type { TranslationParams } from '../../core/i18n/translation.service';
import { DecisionDialogTranslationKeys } from './decision-dialog-translation-keys';

/**
 * The shortest reason the dialog accepts by default (characters, after trimming). Mirror the server validator's
 * limit; a screen whose endpoint uses other limits binds `minLength` and `maxLength`.
 */
export const DECISION_REASON_MIN_LENGTH = 5;

/** The longest reason the dialog accepts by default (characters, after trimming). */
export const DECISION_REASON_MAX_LENGTH = 500;

/** The problem-details field the server reports reason errors under (camelCase, as in problem+json `errors`). */
export const DECISION_REASON_FIELD = 'reason';

/** Why a reason is not acceptable. Integers, like every enum the starter owns. */
export enum ReasonProblem {
  Required = 1,
  TooShort = 2,
  TooLong = 3,
}

/** The length limits of a reason, in characters after trimming. */
export interface ReasonLimits {
  readonly minLength: number;
  readonly maxLength: number;
}

/** The default limits ({@link DECISION_REASON_MIN_LENGTH}, {@link DECISION_REASON_MAX_LENGTH}). */
export const DEFAULT_REASON_LIMITS: ReasonLimits = {
  minLength: DECISION_REASON_MIN_LENGTH,
  maxLength: DECISION_REASON_MAX_LENGTH,
};

/** The reason as it is sent: surrounding white space removed. */
export function normalizeReason(text: string | null | undefined): string {
  return (text ?? '').trim();
}

/** What is wrong with the reason, or null when it is acceptable. Blank counts as missing. */
export function reasonProblem(text: string | null | undefined, limits: ReasonLimits = DEFAULT_REASON_LIMITS): ReasonProblem | null {
  const length = normalizeReason(text).length;
  if (length === 0) {
    return ReasonProblem.Required;
  }
  if (length < limits.minLength) {
    return ReasonProblem.TooShort;
  }
  return length > limits.maxLength ? ReasonProblem.TooLong : null;
}

/** The message (translation key and its parameters) for a reason problem. */
export function reasonProblemMessage(problem: ReasonProblem, limits: ReasonLimits): { readonly key: string; readonly params?: TranslationParams } {
  switch (problem) {
    case ReasonProblem.Required:
      return { key: DecisionDialogTranslationKeys.ReasonRequired };
    case ReasonProblem.TooShort:
      return { key: DecisionDialogTranslationKeys.ReasonTooShort, params: { min: limits.minLength } };
    case ReasonProblem.TooLong:
      return { key: DecisionDialogTranslationKeys.ReasonTooLong, params: { max: limits.maxLength } };
  }
}
