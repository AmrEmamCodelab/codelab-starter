import { describe, expect, it } from 'vitest';
import { applyDevAutofill, devAutofillSetProblems } from '../../../shared/dev-autofill/dev-autofill.fill';
import { contactPhoneProblem } from '../data';
import { SERVICE_REQUEST_WIZARD_FORM_KEY, createServiceRequestWizardForm } from '../requester/wizard/service-request-wizard.form';
import { SERVICE_REQUESTS_DEV_AUTOFILL_SETS, SERVICE_REQUEST_WIZARD_AUTOFILL, samplePhone } from './service-request-autofill';

describe('the Service Request wizard autofill set', () => {
  it('is a well-formed set for the wizard form', () => {
    expect(devAutofillSetProblems([...SERVICE_REQUESTS_DEV_AUTOFILL_SETS])).toEqual([]);
    expect(SERVICE_REQUEST_WIZARD_AUTOFILL.formKey).toBe(SERVICE_REQUEST_WIZARD_FORM_KEY);
    expect(SERVICE_REQUEST_WIZARD_AUTOFILL.labelKey).toContain('.devAutofill.');
  });

  it('builds a phone number of the accepted shape', () => {
    expect(contactPhoneProblem(samplePhone())).toBeNull();
  });

  it('fills the wizard with values that pass every rule it sets, leaving the category to the developer', () => {
    const form = createServiceRequestWizardForm(() => '2026-11-06');
    const summary = applyDevAutofill(form, SERVICE_REQUEST_WIZARD_AUTOFILL);
    expect(summary.unknown).toEqual([]);
    expect(summary.invalid).toEqual([]);
    expect([...summary.filled].sort()).toEqual(['contactPhone', 'description', 'title']);
    expect(form.controls.categoryId.value).toBeNull();
  });
});
