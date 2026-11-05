/**
 * One step of a <dga-stepper>. `label` and `description` are already translated by the caller.
 *
 * - `completed` marks a step done independently of its position: a non-linear flow (any order) or a step the user
 *   finished and then went back from. In a linear stepper every step before the active one counts as completed anyway.
 * - `invalid` flags a step that needs attention (for example server errors mapped to it); it wins over completed.
 * - `disabled` takes a step out of navigation (it is still shown).
 */
export interface DgaStep {
  readonly label: string;
  readonly description?: string;
  readonly completed?: boolean;
  readonly invalid?: boolean;
  readonly disabled?: boolean;
}

/** Where a step stands: the visual and spoken state of its bullet. */
export const DgaStepState = {
  Completed: 'completed',
  Current: 'current',
  Upcoming: 'upcoming',
  Invalid: 'invalid',
} as const;
export type DgaStepState = (typeof DgaStepState)[keyof typeof DgaStepState];

/** The axis the step trail runs along. */
export const DgaStepperOrientation = {
  Horizontal: 'horizontal',
  Vertical: 'vertical',
} as const;
export type DgaStepperOrientation = (typeof DgaStepperOrientation)[keyof typeof DgaStepperOrientation];

/** The step at `index` of a stepper whose current step is `active` (already clamped). */
export interface DgaStepPosition {
  readonly index: number;
  readonly active: number;
  readonly step: DgaStep;
  /** Linear: every step before the active one is completed, and only those (or completed ones) can be revisited. */
  readonly linear: boolean;
}

const FIRST_STEP = 0;

/**
 * The active step index clamped to the steps: NaN or negative gives the first step, and anything past the last step
 * gives `count` (every step completed, none current).
 */
export function clampActiveStep(active: number, count: number): number {
  if (Number.isNaN(active)) return FIRST_STEP;
  return Math.min(count, Math.max(FIRST_STEP, Math.trunc(active)));
}

/**
 * The state of one step. The active step is always current; otherwise `invalid` wins, then completion (the step's own
 * flag, or its position before the active step in a linear stepper), else upcoming.
 */
export function dgaStepState({ index, active, step, linear }: DgaStepPosition): DgaStepState {
  if (index === active) return DgaStepState.Current;
  if (step.invalid) return DgaStepState.Invalid;
  if (step.completed || (linear && index < active)) return DgaStepState.Completed;
  return DgaStepState.Upcoming;
}

/**
 * True when a navigable stepper lets the user jump to this step: never a disabled step or the current one; in a linear
 * stepper only a step before the active one or one marked completed; in a non-linear stepper any other step.
 */
export function isDgaStepSelectable({ index, active, step, linear }: DgaStepPosition): boolean {
  if (step.disabled || index === active) return false;
  if (!linear) return true;
  return index < active || step.completed === true;
}
