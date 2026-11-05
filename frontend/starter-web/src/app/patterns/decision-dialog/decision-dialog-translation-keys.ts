/**
 * Every translation key the decision dialog renders on its own, under `patterns.decision-dialog.*` in
 * `src/assets/i18n/ar.json` and `en.json`. The title and the confirm label are the screen's keys (usually the action's).
 */
export const DecisionDialogTranslationKeys = {
  /** Default label of the reason field. */
  Reason: 'patterns.decision-dialog.reason',
  /** `{{count}}`, `{{max}}`: characters typed of the maximum. */
  Counter: 'patterns.decision-dialog.counter',
  ReasonRequired: 'patterns.decision-dialog.reason-required',
  /** `{{min}}`. */
  ReasonTooShort: 'patterns.decision-dialog.reason-too-short',
  /** `{{max}}`. */
  ReasonTooLong: 'patterns.decision-dialog.reason-too-long',
  /** Default confirm label when the screen gives none. */
  Confirm: 'patterns.decision-dialog.confirm',
} as const;

/** The flat list of every decision-dialog key. */
export const DECISION_DIALOG_TRANSLATION_KEYS: readonly string[] = Object.values(DecisionDialogTranslationKeys);
