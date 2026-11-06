import type { WizardFieldSteps } from '../../../patterns';
import { ServiceRequestDraftStep } from './service-request.models';
import {
  SERVICE_REQUEST_SCOPE_FIELDS,
  ServiceRequestFields,
  ServiceRequestValidationScopes,
  type ServiceRequestField,
  type ServiceRequestValidationScope,
} from './service-request-rules';
import { ServiceRequestTranslationKeys } from './service-requests-translation-keys';

/** The step ids of the wizard (the `<ng-template wizardStep="...">` names and the `WizardFieldSteps` targets). */
export const ServiceRequestStepIds = {
  Request: 'request',
  Details: 'details',
  Attachments: 'attachments',
} as const;

export type ServiceRequestStepId = (typeof ServiceRequestStepIds)[keyof typeof ServiceRequestStepIds];

/**
 * One wizard step of the sample. A screen turns it into the pattern's `WizardStepDef` by adding its `valid` signal.
 * - `draftStep`: the `step` a draft save sends (the server validates only that step).
 * - `scope`: what `validateServiceRequest` checks before Next (step 3 checks everything, as the submit does).
 * - `fields`: the controls the step owns.
 */
export interface ServiceRequestStep {
  readonly id: ServiceRequestStepId;
  readonly titleKey: string;
  readonly descriptionKey: string;
  readonly draftStep: ServiceRequestDraftStep;
  readonly scope: ServiceRequestValidationScope;
  readonly fields: readonly ServiceRequestField[];
}

/** The 3 steps, in order: request (category, title), details (description, phone, date), attachments + review. */
export const SERVICE_REQUEST_STEPS: readonly ServiceRequestStep[] = Object.freeze([
  {
    id: ServiceRequestStepIds.Request,
    titleKey: ServiceRequestTranslationKeys.StepRequestTitle,
    descriptionKey: ServiceRequestTranslationKeys.StepRequestDescription,
    draftStep: ServiceRequestDraftStep.Request,
    scope: ServiceRequestValidationScopes.Step1,
    fields: SERVICE_REQUEST_SCOPE_FIELDS[ServiceRequestValidationScopes.Step1],
  },
  {
    id: ServiceRequestStepIds.Details,
    titleKey: ServiceRequestTranslationKeys.StepDetailsTitle,
    descriptionKey: ServiceRequestTranslationKeys.StepDetailsDescription,
    draftStep: ServiceRequestDraftStep.Details,
    scope: ServiceRequestValidationScopes.Step2,
    fields: SERVICE_REQUEST_SCOPE_FIELDS[ServiceRequestValidationScopes.Step2],
  },
  {
    id: ServiceRequestStepIds.Attachments,
    titleKey: ServiceRequestTranslationKeys.StepAttachmentsTitle,
    descriptionKey: ServiceRequestTranslationKeys.StepAttachmentsDescription,
    draftStep: ServiceRequestDraftStep.Attachments,
    scope: ServiceRequestValidationScopes.Submit,
    fields: [],
  },
]);

/** The step ids in order (the `stepIds` argument of the pattern's error helpers). */
export const SERVICE_REQUEST_STEP_IDS: readonly ServiceRequestStepId[] = SERVICE_REQUEST_STEPS.map((step) => step.id);

/** Which step owns each server field (the wizard pattern's `[fieldSteps]`), so a refused submit opens that step. */
export const SERVICE_REQUEST_FIELD_STEPS: WizardFieldSteps = Object.freeze({
  [ServiceRequestFields.CategoryId]: ServiceRequestStepIds.Request,
  [ServiceRequestFields.Title]: ServiceRequestStepIds.Request,
  [ServiceRequestFields.Description]: ServiceRequestStepIds.Details,
  [ServiceRequestFields.ContactPhone]: ServiceRequestStepIds.Details,
  [ServiceRequestFields.PreferredDate]: ServiceRequestStepIds.Details,
});

/** The label key of a field (`serviceRequests.fields.<field>`), for error summaries and review rows. */
export const SERVICE_REQUEST_FIELD_LABEL_KEYS: Readonly<Record<ServiceRequestField, string>> = Object.freeze({
  [ServiceRequestFields.CategoryId]: ServiceRequestTranslationKeys.FieldCategory,
  [ServiceRequestFields.Title]: ServiceRequestTranslationKeys.FieldTitle,
  [ServiceRequestFields.Description]: ServiceRequestTranslationKeys.FieldDescription,
  [ServiceRequestFields.ContactPhone]: ServiceRequestTranslationKeys.FieldContactPhone,
  [ServiceRequestFields.PreferredDate]: ServiceRequestTranslationKeys.FieldPreferredDate,
});
