/** One option of a <dga-select>. `label` is already translated by the caller. */
export interface DgaSelectOption<T> {
  readonly value: T;
  readonly label: string;
  readonly disabled?: boolean;
}

/** Decides whether two option values are the same choice (for example two objects with the same id). */
export type DgaSelectCompareWith<T> = (a: T, b: T) => boolean;

/** The default comparison: identity, with NaN equal to itself (`Object.is`). */
export const dgaSelectDefaultCompare: DgaSelectCompareWith<unknown> = Object.is;

/** The native value of the empty (placeholder) option; real options use their index. */
export const DGA_SELECT_EMPTY_VALUE = '';

const NOT_FOUND = -1;

/** The index of the option that holds `value`, or -1 when the value is null or matches no option. */
export function selectedOptionIndex<T>(options: readonly DgaSelectOption<T>[], value: T | null, compareWith: DgaSelectCompareWith<T>): number {
  if (value === null) return NOT_FOUND;
  return options.findIndex((option) => compareWith(option.value, value));
}

/** The native <option> value of the option at `index`. */
export function optionNativeValue(index: number): string {
  return String(index);
}

/**
 * The option a native select value stands for: the empty value or anything that is not an option index gives null.
 */
export function optionForNativeValue<T>(options: readonly DgaSelectOption<T>[], nativeValue: string): DgaSelectOption<T> | null {
  if (nativeValue === DGA_SELECT_EMPTY_VALUE) return null;
  const index = Number(nativeValue);
  return Number.isInteger(index) ? (options[index] ?? null) : null;
}
