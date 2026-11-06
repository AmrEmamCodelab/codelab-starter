import type { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';
import {
  ServiceRequestValidationCodes,
  categoryIdProblem,
  contactPhoneProblem,
  descriptionProblem,
  preferredDateProblem,
  serviceRequestMessageKey,
  titleProblem,
  type ServiceRequestField,
  type ServiceRequestValidationCode,
} from './service-request-rules';

/*
 * Reactive-forms validators for the wizard controls. Each one runs the SAME rule function as
 * `validateServiceRequest` (service-request-rules.ts), so a control and the parity-tested pure check can never
 * disagree. A failing control carries `{ <CODE>: true }` (one code); `serviceRequestControlMessageKey` turns it into
 * the translation key a `dga-*` input shows.
 */

type FieldRule = (value: string | null | undefined) => ServiceRequestValidationCode | null;

function textValue(control: AbstractControl): string | null {
  const value: unknown = control.value;
  return typeof value === 'string' ? value : null;
}

function fromRule(rule: FieldRule): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const code = rule(textValue(control));
    return code === null ? null : { [code]: true };
  };
}

/** Today as `YYYY-MM-DD` in the app time zone, read on every check (a wizard left open past midnight stays right). */
export type TodaySource = () => string;

/** The validators of the wizard controls, by field. `preferredDate` needs the app's today. */
export const serviceRequestValidators = {
  categoryId: fromRule(categoryIdProblem),
  title: fromRule(titleProblem),
  description: fromRule(descriptionProblem),
  contactPhone: fromRule(contactPhoneProblem),
  preferredDate: (today: TodaySource): ValidatorFn => fromRule((value) => preferredDateProblem(value, today())),
} as const satisfies Readonly<Record<ServiceRequestField, ValidatorFn | ((today: TodaySource) => ValidatorFn)>>;

const CODES: readonly string[] = Object.values(ServiceRequestValidationCodes);

/** The translation key of the control's validation message (`serviceRequests.errors.<CODE>`), or null when valid. */
export function serviceRequestControlMessageKey(errors: ValidationErrors | null | undefined): string | null {
  if (!errors) return null;
  const code = CODES.find((candidate) => errors[candidate] === true);
  return code === undefined ? null : serviceRequestMessageKey(code);
}
