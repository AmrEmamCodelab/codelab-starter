import type { AbstractControl, FormGroup } from '@angular/forms';
import { DEV_AUTOFILL_LABEL_SEGMENT } from './dev-autofill-keys';
import type { DevAutofillSet, DevAutofillSummary } from './dev-autofill.model';

const KEY_SEPARATOR = '.';

/** The sets registered for one form, in registration order. */
export function devAutofillSetsFor(sets: readonly DevAutofillSet[], formKey: string): readonly DevAutofillSet[] {
  return sets.filter((set) => set.formKey === formKey);
}

/** A factory is called (once per fill); any other value is used as it is. */
export function resolveDevAutofillValue(value: unknown): unknown {
  return typeof value === 'function' ? (value as () => unknown)() : value;
}

/** True when an i18n key has the `devAutofill` segment the production check looks for. */
export function hasDevAutofillLabelSegment(labelKey: string): boolean {
  return labelKey.split(KEY_SEPARATOR).includes(DEV_AUTOFILL_LABEL_SEGMENT);
}

/**
 * What is wrong with a list of sets, one message per problem (empty when they are fine): an empty form key, a label
 * key without the `devAutofill` segment, a set without values, or two sets of one form with the same label key.
 */
export function devAutofillSetProblems(sets: readonly DevAutofillSet[]): readonly string[] {
  const problems: string[] = [];
  const seen = new Set<string>();
  for (const set of sets) {
    const name = `'${set.formKey}' / '${set.labelKey}'`;
    if (set.formKey.trim() === '') problems.push(`${name}: the form key is empty.`);
    if (!hasDevAutofillLabelSegment(set.labelKey)) {
      problems.push(`${name}: the label key needs a '${DEV_AUTOFILL_LABEL_SEGMENT}' segment.`);
    }
    if (Object.keys(set.values).length === 0) problems.push(`${name}: the set has no values.`);
    const identity = `${set.formKey}${KEY_SEPARATOR}${set.labelKey}`;
    if (seen.has(identity)) problems.push(`${name}: registered twice.`);
    seen.add(identity);
  }
  return problems;
}

function write(control: AbstractControl, value: unknown): void {
  control.patchValue(value);
  control.markAsDirty();
  control.markAsTouched();
}

/**
 * Writes a set into a form the way a fast typist would: every enabled control the set names gets its value and is
 * marked dirty and touched, so validation messages and dirty guards behave as after real input. Disabled controls
 * are left alone, paths the form does not have are reported, never thrown. Values are written in the set's key
 * order (put a parent field before the field it drives).
 */
export function applyDevAutofill(form: FormGroup, set: DevAutofillSet): DevAutofillSummary {
  const filled: string[] = [];
  const skipped: string[] = [];
  const unknown: string[] = [];
  for (const [path, value] of Object.entries(set.values)) {
    const control = form.get(path);
    if (control === null) {
      unknown.push(path);
    } else if (control.disabled) {
      skipped.push(path);
    } else {
      write(control, resolveDevAutofillValue(value));
      filled.push(path);
    }
  }
  const invalid = filled.filter((path) => form.get(path)?.invalid === true);
  return { formKey: set.formKey, labelKey: set.labelKey, filled, skipped, unknown, invalid };
}
