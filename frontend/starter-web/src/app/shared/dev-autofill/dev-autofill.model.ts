import { InjectionToken } from '@angular/core';

/**
 * A value computed when the button is pressed, not when the set is declared: a date relative to today, a unique
 * title. Called with no arguments, once per fill.
 */
export type DevAutofillValueFactory = () => unknown;

/**
 * One named, known-good data set for one form. A form may have several (valid, edge case, ...): each one is a button.
 *
 * - `formKey` names the form; the screen passes the same key to `<dga-dev-autofill>` or `[devAutofill]`.
 * - `labelKey` is the i18n key of the button text (ar + en). It must contain the `devAutofill` segment
 *   (`serviceRequests.devAutofill.valid`), which is what `tools/ci/no-dev-autofill.mjs` looks for in a production
 *   bundle; `provideDevAutofill` refuses a set without it.
 * - `values` maps a control path (`title`, `address.city`, as `FormGroup.get` reads it) to its value, or to a
 *   {@link DevAutofillValueFactory}. A group or array path takes a partial object or array (`patchValue`).
 */
export interface DevAutofillSet {
  readonly formKey: string;
  readonly labelKey: string;
  readonly values: Readonly<Record<string, unknown>>;
}

/** What one press did, by control path. */
export interface DevAutofillSummary {
  readonly formKey: string;
  readonly labelKey: string;
  /** Paths written (and marked dirty and touched). */
  readonly filled: readonly string[];
  /** Paths left alone because their control is disabled. */
  readonly skipped: readonly string[];
  /** Paths the form does not have: the set is stale. */
  readonly unknown: readonly string[];
  /** Filled paths whose control is still invalid afterwards: the set no longer satisfies the form's rules. */
  readonly invalid: readonly string[];
}

/**
 * Every registered data set (a multi provider: one entry per set, from every `provideDevAutofill` call). Absent
 * when nothing is registered and in a production build, so inject it with `{ optional: true }`.
 *
 * Not re-exported from the barrel: only the real implementation reads it, so a production bundle never includes it.
 */
export const DEV_AUTOFILL_SETS = new InjectionToken<readonly DevAutofillSet[]>('DEV_AUTOFILL_SETS');
