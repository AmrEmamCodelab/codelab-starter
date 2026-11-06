import { describe, expect, it } from 'vitest';
import { HttpStatus, type ApiError } from '@codelab/ng-security';
import { NO_FIELD_ERRORS } from '../../../../patterns';
import { SERVICE_REQUEST_STEPS, ServiceRequestValidationCodes } from '../../data';
import { TEST_CATEGORY_ID, TEST_PHONE, TEST_TODAY, aDraft } from '../requester.testing';
import {
  createServiceRequestWizardForm,
  draftFieldsOf,
  fieldMessageKey,
  firstStepIndexWithErrors,
  patchFromDraft,
  resumeStepIndex,
  stepIsValid,
  withoutFieldErrors,
} from './service-request-wizard.form';

const today = () => TEST_TODAY;
const DESCRIPTION = 'A description that is long enough.';
const [requestStep, detailsStep, attachmentsStep] = SERVICE_REQUEST_STEPS;

function filledForm() {
  const form = createServiceRequestWizardForm(today);
  patchFromDraft(form, aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE }));
  return form;
}

describe('the wizard form', () => {
  it('validates each step with the data-layer rules', () => {
    const form = createServiceRequestWizardForm(today);
    expect(requestStep && stepIsValid(form, requestStep)).toBe(false);
    expect(attachmentsStep && stepIsValid(form, attachmentsStep)).toBe(true);
    form.controls.categoryId.setValue(TEST_CATEGORY_ID);
    form.controls.title.setValue('abcd');
    expect(requestStep && stepIsValid(form, requestStep)).toBe(false);
    form.controls.title.setValue('abcde');
    expect(requestStep && stepIsValid(form, requestStep)).toBe(true);
    expect(detailsStep && stepIsValid(form, detailsStep)).toBe(false);
  });

  it('refuses a preferred date before today and accepts today', () => {
    const form = filledForm();
    form.controls.preferredDate.setValue('2026-11-05');
    expect(detailsStep && stepIsValid(form, detailsStep)).toBe(false);
    form.controls.preferredDate.setValue(TEST_TODAY);
    expect(detailsStep && stepIsValid(form, detailsStep)).toBe(true);
  });

  it('sends empty text as null and text as typed', () => {
    const form = filledForm();
    form.controls.title.setValue(' A valid title ');
    form.controls.preferredDate.setValue('');
    expect(draftFieldsOf(form)).toEqual({
      categoryId: TEST_CATEGORY_ID,
      title: ' A valid title ',
      description: DESCRIPTION,
      contactPhone: TEST_PHONE,
      preferredDate: null,
    });
  });

  it('drops a category value that is not a lookup token', () => {
    const form = filledForm();
    form.controls.categoryId.setValue('7');
    expect(draftFieldsOf(form).categoryId).toBeNull();
  });
});

describe('resumeStepIndex', () => {
  it('opens a draft on the first step that still needs input', () => {
    expect(resumeStepIndex(aDraft(), TEST_TODAY)).toBe(0);
    expect(resumeStepIndex(aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title' }), TEST_TODAY)).toBe(1);
    expect(
      resumeStepIndex(aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE }), TEST_TODAY),
    ).toBe(2);
  });

  it('sends a draft whose saved date has passed back to the details step', () => {
    const draft = aDraft({ categoryId: TEST_CATEGORY_ID, title: 'A valid title', description: DESCRIPTION, contactPhone: TEST_PHONE, preferredDate: '2026-11-01' });
    expect(resumeStepIndex(draft, TEST_TODAY)).toBe(1);
  });
});

describe('server field errors', () => {
  it('finds the first step that owns a field error', () => {
    expect(firstStepIndexWithErrors(NO_FIELD_ERRORS)).toBeNull();
    expect(firstStepIndexWithErrors({ contactPhone: ['k'] })).toBe(1);
    expect(firstStepIndexWithErrors({ contactPhone: ['k'], title: ['k'] })).toBe(0);
    expect(firstStepIndexWithErrors({ somethingElse: ['k'] })).toBeNull();
  });

  it('shows the server message first, then the client one once touched or revealed', () => {
    const form = createServiceRequestWizardForm(today);
    const quiet = { serverErrors: NO_FIELD_ERRORS, revealAll: false };
    expect(fieldMessageKey(form, 'title', quiet)).toBeNull();
    expect(fieldMessageKey(form, 'title', { ...quiet, revealAll: true })).toBe(
      `serviceRequests.errors.${ServiceRequestValidationCodes.TitleRequired}`,
    );
    form.controls.title.markAsTouched();
    expect(fieldMessageKey(form, 'title', quiet)).toBe(`serviceRequests.errors.${ServiceRequestValidationCodes.TitleRequired}`);
    expect(fieldMessageKey(form, 'title', { serverErrors: { title: ['server.key'] }, revealAll: false })).toBe('server.key');
  });

  it('hands the pattern the refusal without its field errors', () => {
    const refusal: ApiError = { status: HttpStatus.badRequest, code: 'VALIDATION_FAILED', fieldErrors: [{ field: 'title', message: 'X' }] };
    expect(withoutFieldErrors(refusal)).toEqual({ ...refusal, fieldErrors: [] });
    const plain: ApiError = { status: HttpStatus.conflict, code: 'ATTACHMENTS_REQUIRED_MISSING', fieldErrors: [] };
    expect(withoutFieldErrors(plain)).toBe(plain);
    expect(withoutFieldErrors(null)).toBeNull();
  });
});
