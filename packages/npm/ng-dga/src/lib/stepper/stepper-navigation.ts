import { DgaStepperOrientation } from './step-state';

/** The keys a navigable stepper handles to move focus between its step buttons. */
export const DgaStepperKey = {
  ArrowLeft: 'ArrowLeft',
  ArrowRight: 'ArrowRight',
  ArrowUp: 'ArrowUp',
  ArrowDown: 'ArrowDown',
  Home: 'Home',
  End: 'End',
} as const;

/** How the arrow keys map onto the trail. */
export interface DgaStepperKeyContext {
  readonly orientation: DgaStepperOrientation;
  readonly rightToLeft: boolean;
}

const FORWARD = 1;
const BACKWARD = -1;

/** +1 / -1 for an arrow key along the trail (mirrored in RTL for a horizontal trail), or null for any other key. */
function arrowDirection(key: string, { orientation, rightToLeft }: DgaStepperKeyContext): number | null {
  if (orientation === DgaStepperOrientation.Vertical) {
    if (key === DgaStepperKey.ArrowDown) return FORWARD;
    if (key === DgaStepperKey.ArrowUp) return BACKWARD;
    return null;
  }
  const forwardKey = rightToLeft ? DgaStepperKey.ArrowLeft : DgaStepperKey.ArrowRight;
  const backwardKey = rightToLeft ? DgaStepperKey.ArrowRight : DgaStepperKey.ArrowLeft;
  if (key === forwardKey) return FORWARD;
  if (key === backwardKey) return BACKWARD;
  return null;
}

function focusableFrom(focusable: readonly boolean[], start: number, step: number): number | null {
  for (let index = start; index >= 0 && index < focusable.length; index += step) {
    if (focusable[index]) return index;
  }
  return null;
}

/**
 * The step button a key moves focus to from `current`, skipping steps that cannot take focus and stopping at the ends
 * (no wrap: a trail has a first and a last step), or null when the key is not handled or nothing is further on.
 * `focusable[i]` says whether step i's button can take focus.
 */
export function nextFocusableStep(focusable: readonly boolean[], current: number, key: string, context: DgaStepperKeyContext): number | null {
  if (key === DgaStepperKey.Home) return focusableFrom(focusable, 0, FORWARD);
  if (key === DgaStepperKey.End) return focusableFrom(focusable, focusable.length - 1, BACKWARD);
  const direction = arrowDirection(key, context);
  return direction === null ? null : focusableFrom(focusable, current + direction, direction);
}

/** The step in the Tab order (roving tabindex): the active step when it can take focus, else the first that can. */
export function rovingStepIndex(focusable: readonly boolean[], active: number): number | null {
  if (focusable[active]) return active;
  return focusableFrom(focusable, 0, FORWARD);
}
