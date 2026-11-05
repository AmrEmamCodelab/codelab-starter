/**
 * Every translation key the wizard pattern renders on its own (step titles come from the screen's step definitions).
 * All live under `patterns.wizard.*` in `src/assets/i18n/ar.json` and `en.json`.
 */
export const WizardTranslationKeys = {
  /** Accessible name of the step trail. */
  Progress: 'patterns.wizard.progress',
  /** `{{current}}`, `{{total}}`. */
  StepOf: 'patterns.wizard.step-of',
  Completed: 'patterns.wizard.completed',
  Current: 'patterns.wizard.current',
  Invalid: 'patterns.wizard.invalid',
  Next: 'patterns.wizard.next',
  Back: 'patterns.wizard.back',
  SaveDraft: 'patterns.wizard.save-draft',
  Submit: 'patterns.wizard.submit',
  Cancel: 'patterns.wizard.cancel',
  /** Default title of the review step. */
  Review: 'patterns.wizard.review',
  /** Shown when Next or Submit is refused because a step is incomplete. */
  FixStep: 'patterns.wizard.fix-step',
  ErrorsTitle: 'patterns.wizard.errors-title',
  /** `{{step}}`: the translated step title. */
  GoToStep: 'patterns.wizard.go-to-step',
} as const;

/** The flat list of every wizard key. */
export const WIZARD_TRANSLATION_KEYS: readonly string[] = Object.values(WizardTranslationKeys);
