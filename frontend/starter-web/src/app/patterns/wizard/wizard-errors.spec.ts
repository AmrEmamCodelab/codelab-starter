import { describe, expect, it } from 'vitest';
import { NO_STEP_ERRORS, errorsByStep, errorsOfStep, firstStepWithErrors, stepIdForField } from './wizard-errors';

const FIELD_STEPS = { applicant: 'applicant', 'applicant.address': 'address', documents: 'documents' };
const STEP_IDS = ['applicant', 'address', 'documents', 'review'];

describe('stepIdForField', () => {
  it('maps a field, its sub-paths and its indexed items to the owning step', () => {
    expect(stepIdForField('applicant', FIELD_STEPS)).toBe('applicant');
    expect(stepIdForField('applicant.email', FIELD_STEPS)).toBe('applicant');
    expect(stepIdForField('documents[0].name', FIELD_STEPS)).toBe('documents');
  });

  it('prefers the longest matching prefix and ignores case', () => {
    expect(stepIdForField('applicant.address.city', FIELD_STEPS)).toBe('address');
    expect(stepIdForField('Applicant.Address.City', FIELD_STEPS)).toBe('address');
  });

  it('does not match a field that merely starts with the same letters', () => {
    expect(stepIdForField('applicantType', FIELD_STEPS)).toBeNull();
    expect(stepIdForField('comment', FIELD_STEPS)).toBeNull();
  });
});

describe('errorsByStep', () => {
  it('splits the field errors by owning step and keeps the rest unmapped', () => {
    const split = errorsByStep(
      { 'applicant.email': ['errors.EMAIL_INVALID'], 'applicant.address.city': ['errors.CITY_REQUIRED'], reference: ['errors.X'] },
      FIELD_STEPS,
      STEP_IDS,
    );
    expect(split.byStep).toEqual({
      applicant: { 'applicant.email': ['errors.EMAIL_INVALID'] },
      address: { 'applicant.address.city': ['errors.CITY_REQUIRED'] },
    });
    expect(split.unmapped).toEqual({ reference: ['errors.X'] });
  });

  it('leaves a field unmapped when its step is not in the wizard', () => {
    const split = errorsByStep({ 'documents.file': ['errors.F'] }, FIELD_STEPS, ['applicant']);
    expect(split.byStep).toEqual({});
    expect(split.unmapped).toEqual({ 'documents.file': ['errors.F'] });
  });

  it('gives the shared empty result when there are no errors', () => {
    expect(errorsByStep({}, FIELD_STEPS, STEP_IDS)).toBe(NO_STEP_ERRORS);
  });
});

describe('firstStepWithErrors and errorsOfStep', () => {
  it('finds the first step, in wizard order, that has errors', () => {
    const split = errorsByStep({ 'documents.file': ['errors.F'], 'applicant.address.zip': ['errors.Z'] }, FIELD_STEPS, STEP_IDS);
    expect(firstStepWithErrors(STEP_IDS, split)).toBe(1);
    expect(firstStepWithErrors(STEP_IDS, NO_STEP_ERRORS)).toBeNull();
  });

  it('returns a step its own errors, or none', () => {
    const split = errorsByStep({ 'documents.file': ['errors.F'] }, FIELD_STEPS, STEP_IDS);
    expect(errorsOfStep(split, 'documents')).toEqual({ 'documents.file': ['errors.F'] });
    expect(errorsOfStep(split, 'applicant')).toEqual({});
  });
});
