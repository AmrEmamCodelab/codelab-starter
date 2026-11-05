import type { Signal } from '@angular/core';

/**
 * One step of a wizard, defined by the screen.
 *
 * - `id`: stable key of the step; server field errors are mapped to it (see `WizardFieldSteps`), and the step's
 *   `<ng-template wizardStep="id">` renders its body.
 * - `titleKey` / `descriptionKey`: translation keys shown in the step trail and above the body.
 * - `valid`: the step's OWN validity, usually `toSignal(group.statusChanges)` or a `computed` over the screen's form
 *   section. Next refuses to leave an invalid step; Submit refuses while any step is invalid.
 */
export interface WizardStepDef {
  readonly id: string;
  readonly titleKey: string;
  readonly descriptionKey?: string;
  readonly valid: Signal<boolean>;
}

/** The id of the review step the pattern appends when the screen projects `<ng-template wizardReview>`. */
export const WIZARD_REVIEW_STEP_ID = 'review';

/** The validity of each step in order and where the user is. */
export interface WizardPosition {
  readonly validity: readonly boolean[];
  readonly current: number;
}

/** The answer of a navigation guard: allowed, or refused because of the step at `blockedAt` (null: out of range). */
export interface WizardGuardResult {
  readonly allowed: boolean;
  readonly blockedAt: number | null;
}

const ALLOWED: WizardGuardResult = Object.freeze({ allowed: true, blockedAt: null });
const OUT_OF_RANGE: WizardGuardResult = Object.freeze({ allowed: false, blockedAt: null });

function blocked(at: number): WizardGuardResult {
  return { allowed: false, blockedAt: at };
}

/** A half-open range of step indexes, `[from, to)`. */
export interface WizardStepRange {
  readonly from: number;
  readonly to: number;
}

/** Index of the first invalid step in `range` (default: every step), or null when all of them are valid. */
export function firstInvalidStep(validity: readonly boolean[], range?: WizardStepRange): number | null {
  const from = Math.max(0, range?.from ?? 0);
  const to = Math.min(range?.to ?? validity.length, validity.length);
  for (let index = from; index < to; index += 1) {
    if (!validity[index]) return index;
  }
  return null;
}

/**
 * May the user move from `current` to `target`? Going back (or staying) is always allowed and never validated.
 * Going forward in a LINEAR wizard needs every step from the current one up to the target to be valid (the first
 * invalid one blocks); a NON-LINEAR wizard lets the user jump anywhere. Out-of-range targets are refused.
 */
export function guardStepChange(position: WizardPosition, target: number, linear: boolean): WizardGuardResult {
  if (!Number.isInteger(target) || target < 0 || target >= position.validity.length) return OUT_OF_RANGE;
  if (target <= position.current || !linear) return ALLOWED;
  const invalid = firstInvalidStep(position.validity, { from: position.current, to: target });
  return invalid === null ? ALLOWED : blocked(invalid);
}

/** Next always validates the current step, in linear and non-linear wizards alike ("I am done with this step"). */
export function guardNext(position: WizardPosition): WizardGuardResult {
  return guardStepChange(position, position.current + 1, true);
}

/** Submit needs every step valid; the first invalid one blocks it. */
export function guardSubmit(validity: readonly boolean[]): WizardGuardResult {
  const invalid = firstInvalidStep(validity);
  return invalid === null ? ALLOWED : blocked(invalid);
}

/** The step index clamped to a wizard of `count` steps (NaN and negatives give the first step). */
export function clampWizardStep({ step, count }: { readonly step: number; readonly count: number }): number {
  if (count <= 0 || Number.isNaN(step)) return 0;
  return Math.min(count - 1, Math.max(0, Math.trunc(step)));
}
