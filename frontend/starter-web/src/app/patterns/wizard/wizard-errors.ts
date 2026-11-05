import { NO_FIELD_ERRORS, hasFieldErrors, type FieldErrorMap } from '../editor-dialog/field-errors';

/**
 * Which step owns which server field: a field path, or a path PREFIX, to a step id. `{ applicant: 'applicant' }` sends
 * `applicant`, `applicant.email` and `applicant[0].name` to the `applicant` step; the longest matching prefix wins, so
 * `{ applicant: 'a', 'applicant.address': 'b' }` sends `applicant.address.city` to `b`. Matching ignores case (the API
 * names fields in camelCase, a binder path may not).
 */
export type WizardFieldSteps = Readonly<Record<string, string>>;

/** Server field errors split by the step that owns each field. */
export interface WizardStepErrors {
  /** Step id to the field errors of that step (steps without errors are absent). */
  readonly byStep: Readonly<Record<string, FieldErrorMap>>;
  /** Field errors no step owns: shown in the wizard's error summary. */
  readonly unmapped: FieldErrorMap;
}

/** No errors anywhere. */
export const NO_STEP_ERRORS: WizardStepErrors = Object.freeze({ byStep: Object.freeze({}), unmapped: NO_FIELD_ERRORS });

const PATH_SEPARATORS = ['.', '['] as const;

function ownsField(prefix: string, field: string): boolean {
  return field === prefix || PATH_SEPARATORS.some((separator) => field.startsWith(prefix + separator));
}

/** The step id that owns `field` (longest matching prefix), or null when no step does. */
export function stepIdForField(field: string, fieldSteps: WizardFieldSteps): string | null {
  const normalized = field.toLowerCase();
  let best: { readonly length: number; readonly stepId: string } | null = null;
  for (const [prefix, stepId] of Object.entries(fieldSteps)) {
    const candidate = prefix.toLowerCase();
    if (ownsField(candidate, normalized) && (best === null || candidate.length > best.length)) {
      best = { length: candidate.length, stepId };
    }
  }
  return best?.stepId ?? null;
}

/**
 * Splits a field-error map (`fieldErrorsFrom(apiError)`) by owning step. Only ids in `stepIds` count as owners: a
 * mapping to a step the wizard does not have leaves the field unmapped rather than hiding it.
 */
export function errorsByStep(errors: FieldErrorMap, fieldSteps: WizardFieldSteps, stepIds: readonly string[]): WizardStepErrors {
  if (!hasFieldErrors(errors)) return NO_STEP_ERRORS;
  const known = new Set(stepIds);
  const byStep: Record<string, Record<string, readonly string[]>> = {};
  const unmapped: Record<string, readonly string[]> = {};
  for (const [field, keys] of Object.entries(errors)) {
    const stepId = stepIdForField(field, fieldSteps);
    const target = stepId !== null && known.has(stepId) ? (byStep[stepId] ??= {}) : unmapped;
    target[field] = keys;
  }
  return { byStep, unmapped };
}

/** Index (in `stepIds`) of the first step that has errors, or null. */
export function firstStepWithErrors(stepIds: readonly string[], errors: WizardStepErrors): number | null {
  const index = stepIds.findIndex((id) => hasFieldErrors(errors.byStep[id] ?? NO_FIELD_ERRORS));
  return index < 0 ? null : index;
}

/** The field errors of one step, or none. */
export function errorsOfStep(errors: WizardStepErrors, stepId: string): FieldErrorMap {
  return errors.byStep[stepId] ?? NO_FIELD_ERRORS;
}
