/** One step of a <dga-stepper>. `label` and `description` are already translated by the caller. */
export interface DgaStep {
  readonly label: string;
  readonly description?: string;
}

/** Where a step stands relative to the active step. */
export type DgaStepState = 'completed' | 'current' | 'upcoming';

const FIRST_STEP = 0;

/**
 * The active step index clamped to the steps: NaN or negative gives the first step, and anything past the last step
 * gives `count` (every step completed, none current).
 */
export function clampActiveStep(active: number, count: number): number {
  if (Number.isNaN(active)) return FIRST_STEP;
  return Math.min(count, Math.max(FIRST_STEP, Math.trunc(active)));
}

/** The state of the step at `index` when `active` (already clamped) is the current step. */
export function dgaStepState(index: number, active: number): DgaStepState {
  if (index < active) return 'completed';
  if (index === active) return 'current';
  return 'upcoming';
}
