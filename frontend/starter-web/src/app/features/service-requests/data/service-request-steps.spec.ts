import { describe, expect, it } from 'vitest';
import { errorsByStep, firstStepWithErrors } from '../../../patterns';
import { ServiceRequestDraftStep } from './service-request.models';
import { ServiceRequestFields, ServiceRequestValidationScopes } from './service-request-rules';
import {
  SERVICE_REQUEST_FIELD_LABEL_KEYS,
  SERVICE_REQUEST_FIELD_STEPS,
  SERVICE_REQUEST_STEPS,
  SERVICE_REQUEST_STEP_IDS,
} from './service-request-steps';

describe('SERVICE_REQUEST_STEPS', () => {
  it('has the 3 steps in order with their draft step and scope', () => {
    expect(SERVICE_REQUEST_STEP_IDS).toEqual(['request', 'details', 'attachments']);
    expect(SERVICE_REQUEST_STEPS.map((step) => step.draftStep)).toEqual([
      ServiceRequestDraftStep.Request,
      ServiceRequestDraftStep.Details,
      ServiceRequestDraftStep.Attachments,
    ]);
    expect(SERVICE_REQUEST_STEPS.map((step) => step.scope)).toEqual([
      ServiceRequestValidationScopes.Step1,
      ServiceRequestValidationScopes.Step2,
      ServiceRequestValidationScopes.Submit,
    ]);
  });

  it('gives every field to exactly one step, and the field map agrees with the steps', () => {
    const owned = SERVICE_REQUEST_STEPS.flatMap((step) => step.fields.map((field) => [field, step.id] as const));

    expect(owned.map(([field]) => field).sort()).toEqual(Object.values(ServiceRequestFields).sort());
    expect(Object.fromEntries(owned)).toEqual(SERVICE_REQUEST_FIELD_STEPS);
  });

  it('sends a refused submit to the step that owns the field', () => {
    const errors = errorsByStep({ contactPhone: ['k'], attachments: ['k'] }, SERVICE_REQUEST_FIELD_STEPS, SERVICE_REQUEST_STEP_IDS);

    expect(firstStepWithErrors(SERVICE_REQUEST_STEP_IDS, errors)).toBe(1);
    expect(errors.unmapped).toEqual({ attachments: ['k'] });
  });

  it('uses serviceRequests.* keys for titles, descriptions and field labels', () => {
    const keys = [
      ...SERVICE_REQUEST_STEPS.flatMap((step) => [step.titleKey, step.descriptionKey]),
      ...Object.values(SERVICE_REQUEST_FIELD_LABEL_KEYS),
    ];
    for (const key of keys) {
      expect(key.startsWith('serviceRequests.')).toBe(true);
    }
    expect(SERVICE_REQUEST_FIELD_LABEL_KEYS.contactPhone).toBe('serviceRequests.fields.contactPhone');
  });
});
