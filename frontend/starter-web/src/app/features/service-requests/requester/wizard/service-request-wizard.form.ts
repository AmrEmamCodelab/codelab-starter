import { FormControl, FormGroup, type AbstractControl } from '@angular/forms';
import { tryAsId, type ApiError } from '@codelab/ng-security';
import { NO_FIELD_ERRORS, errorsByStep, firstStepWithErrors, type FieldErrorMap } from '../../../../patterns';
import {
  SERVICE_REQUEST_FIELD_STEPS,
  SERVICE_REQUEST_STEPS,
  SERVICE_REQUEST_STEP_IDS,
  ServiceRequestFields,
  serviceRequestControlMessageKey,
  serviceRequestValidators,
  validateServiceRequest,
  type ServiceRequestDraftFields,
  type ServiceRequestField,
  type ServiceRequestStep,
  type TodaySource,
} from '../../data';

/** The form key of the wizard: its DevAutofill data sets are registered under it (`features/service-requests/dev`). */
export const SERVICE_REQUEST_WIZARD_FORM_KEY = 'service-request-wizard';

/** The id domain of a request category token (`GET api/lookups/{type}` answers family `lookup-items`). */
const CATEGORY_ID_DOMAIN = 'lookup-items';

/** The wizard's controls, one per draft field, named by the camelCase wire names (`ServiceRequestFields`). */
export interface ServiceRequestWizardControls {
  readonly categoryId: FormControl<string | null>;
  readonly title: FormControl<string | null>;
  readonly description: FormControl<string | null>;
  readonly contactPhone: FormControl<string | null>;
  readonly preferredDate: FormControl<string | null>;
}

export type ServiceRequestWizardForm = FormGroup<{ [K in keyof ServiceRequestWizardControls]: ServiceRequestWizardControls[K] }>;

/**
 * The wizard's form: one flat group with every field, each control validated by the data layer's rule functions (the
 * same ones the parity fixture pins). `today` is read on every check, so a wizard left open past midnight stays right.
 */
export function createServiceRequestWizardForm(today: TodaySource): ServiceRequestWizardForm {
  return new FormGroup({
    categoryId: new FormControl<string | null>(null, serviceRequestValidators.categoryId),
    title: new FormControl<string | null>(null, serviceRequestValidators.title),
    description: new FormControl<string | null>(null, serviceRequestValidators.description),
    contactPhone: new FormControl<string | null>(null, serviceRequestValidators.contactPhone),
    preferredDate: new FormControl<string | null>(null, serviceRequestValidators.preferredDate(today)),
  });
}

function textOrNull(value: string | null | undefined): string | null {
  return value === null || value === undefined || value === '' ? null : value;
}

/** The form's values as the draft fields the API takes. Empty text is null; text is sent as typed (no trimming). */
export function draftFieldsOf(form: ServiceRequestWizardForm): ServiceRequestDraftFields {
  const value = form.getRawValue();
  return {
    categoryId: tryAsId(CATEGORY_ID_DOMAIN, textOrNull(value.categoryId)),
    title: textOrNull(value.title),
    description: textOrNull(value.description),
    contactPhone: textOrNull(value.contactPhone),
    preferredDate: textOrNull(value.preferredDate),
  };
}

/** Writes a saved draft into the form (a resume), without marking anything touched. */
export function patchFromDraft(form: ServiceRequestWizardForm, draft: ServiceRequestDraftFields): void {
  form.setValue({
    categoryId: draft.categoryId,
    title: draft.title,
    description: draft.description,
    contactPhone: draft.contactPhone,
    preferredDate: draft.preferredDate,
  });
}

/** The control of a field. */
export function controlOf(form: ServiceRequestWizardForm, field: ServiceRequestField): AbstractControl<string | null> {
  return form.controls[field];
}

/** True when every control the step owns is valid (step 3 owns none: its check is the server's attachment interlock). */
export function stepIsValid(form: ServiceRequestWizardForm, step: ServiceRequestStep): boolean {
  return step.fields.every((field) => controlOf(form, field).valid);
}

/**
 * The step a resumed draft opens on: the first step whose fields fail the shared rules, else the last step (attach
 * and submit). The same `validateServiceRequest` the parity fixture pins decides, so the server would agree.
 */
export function resumeStepIndex(fields: ServiceRequestDraftFields, today: string): number {
  const index = SERVICE_REQUEST_STEPS.findIndex(
    (step) => step.fields.length > 0 && !validateServiceRequest(fields, step.scope, today).valid,
  );
  return index < 0 ? SERVICE_REQUEST_STEPS.length - 1 : index;
}

/** The index of the first step that owns one of the server's field errors, or null. */
export function firstStepIndexWithErrors(errors: FieldErrorMap): number | null {
  if (errors === NO_FIELD_ERRORS) return null;
  return firstStepWithErrors(SERVICE_REQUEST_STEP_IDS, errorsByStep(errors, SERVICE_REQUEST_FIELD_STEPS, SERVICE_REQUEST_STEP_IDS));
}

/**
 * The message key a field shows: the server's first message for it when the last save or submit was refused, else the
 * client rule's message once the user has touched the control (or a step check asked for every message).
 */
export function fieldMessageKey(
  form: ServiceRequestWizardForm,
  field: ServiceRequestField,
  { serverErrors, revealAll }: { readonly serverErrors: FieldErrorMap; readonly revealAll: boolean },
): string | null {
  const server = serverErrors[field]?.[0];
  if (server !== undefined) return server;
  const control = controlOf(form, field);
  return control.touched || revealAll ? serviceRequestControlMessageKey(control.errors) : null;
}

/** Every field, in wizard order (the form's control names). */
export const WIZARD_FIELDS: readonly ServiceRequestField[] = Object.values(ServiceRequestFields);

/**
 * The refusal as the wizard pattern shows it: its message and reference code only. The field errors are shown under
 * each field in this module's own wording (`serviceRequests.errors.<CODE>`), which the pattern's generic mapping
 * (`errors.<CODE>`) cannot render, so they are left out here.
 */
export function withoutFieldErrors(error: ApiError | null): ApiError | null {
  return error === null || error.fieldErrors.length === 0 ? error : { ...error, fieldErrors: [] };
}
