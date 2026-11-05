import type { DgaSelectCompareWith, DgaSelectOption } from '../select/select-options';

/**
 * The pure rules of a multi-select: which values are chosen, toggling and removing them, and moving the active option
 * through a listbox with the keyboard (WAI-ARIA listbox pattern: no wrap-around, disabled options skipped).
 */

/** The keys that move the active option. */
export const DgaListMoveKey = {
  Next: 'ArrowDown',
  Previous: 'ArrowUp',
  First: 'Home',
  Last: 'End',
} as const;
export type DgaListMoveKey = (typeof DgaListMoveKey)[keyof typeof DgaListMoveKey];

/** The keys that toggle the active option. */
export const DgaListToggleKey = {
  Space: ' ',
  Enter: 'Enter',
} as const;
export type DgaListToggleKey = (typeof DgaListToggleKey)[keyof typeof DgaListToggleKey];

/** No active option. */
export const NO_ACTIVE_OPTION = -1;

const MOVE_KEYS: readonly string[] = Object.values(DgaListMoveKey);
const TOGGLE_KEYS: readonly string[] = Object.values(DgaListToggleKey);
const STEP = 1;

export function isListMoveKey(key: string): key is DgaListMoveKey {
  return MOVE_KEYS.includes(key);
}

export function isListToggleKey(key: string): key is DgaListToggleKey {
  return TOGGLE_KEYS.includes(key);
}

/** True when `value` is among the chosen values. */
export function isChosen<T>(chosen: readonly T[], value: T, compareWith: DgaSelectCompareWith<T>): boolean {
  return chosen.some((item) => compareWith(item, value));
}

/** The chosen values with `value` removed (every match). */
export function withoutValue<T>(chosen: readonly T[], value: T, compareWith: DgaSelectCompareWith<T>): T[] {
  return chosen.filter((item) => !compareWith(item, value));
}

/** The chosen values with `value` added at the end, or removed when it is already chosen. */
export function toggleValue<T>(chosen: readonly T[], value: T, compareWith: DgaSelectCompareWith<T>): T[] {
  return isChosen(chosen, value, compareWith) ? withoutValue(chosen, value, compareWith) : [...chosen, value];
}

/** The options that are chosen, in option order (values that match no option are left out). */
export function chosenOptions<T>(
  options: readonly DgaSelectOption<T>[],
  chosen: readonly T[],
  compareWith: DgaSelectCompareWith<T>,
): DgaSelectOption<T>[] {
  return options.filter((option) => isChosen(chosen, option.value, compareWith));
}

function isEnabled<T>(option: DgaSelectOption<T> | undefined): boolean {
  return option !== undefined && option.disabled !== true;
}

function firstEnabledFrom<T>(options: readonly DgaSelectOption<T>[], start: number, step: number): number {
  for (let index = start; index >= 0 && index < options.length; index += step) {
    if (isEnabled(options[index])) return index;
  }
  return NO_ACTIVE_OPTION;
}

/**
 * The active option after a move key: Next and Previous step to the nearest enabled option and stay put at either
 * end; First and Last jump to the first and last enabled option. NO_ACTIVE_OPTION when nothing is enabled.
 */
export function moveActiveOption<T>(options: readonly DgaSelectOption<T>[], move: { readonly from: number; readonly key: DgaListMoveKey }): number {
  switch (move.key) {
    case DgaListMoveKey.First:
      return firstEnabledFrom(options, 0, STEP);
    case DgaListMoveKey.Last:
      return firstEnabledFrom(options, options.length - STEP, -STEP);
    case DgaListMoveKey.Next: {
      const next = firstEnabledFrom(options, move.from + STEP, STEP);
      return next === NO_ACTIVE_OPTION ? initialActiveOption(options, move.from) : next;
    }
    case DgaListMoveKey.Previous: {
      const previous = move.from === NO_ACTIVE_OPTION ? NO_ACTIVE_OPTION : firstEnabledFrom(options, move.from - STEP, -STEP);
      return previous === NO_ACTIVE_OPTION ? initialActiveOption(options, move.from) : previous;
    }
  }
}

/** The option to make active: `preferred` when it is enabled, otherwise the first enabled option. */
export function initialActiveOption<T>(options: readonly DgaSelectOption<T>[], preferred: number): number {
  return isEnabled(options[preferred]) ? preferred : firstEnabledFrom(options, 0, STEP);
}
