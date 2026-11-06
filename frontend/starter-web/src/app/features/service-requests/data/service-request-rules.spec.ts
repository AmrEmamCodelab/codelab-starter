import { parseApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { NO_FIELD_ERRORS } from '../../../patterns';
import {
  CONTACT_PHONE_PATTERN,
  SERVICE_REQUEST_SCOPE_FIELDS,
  ServiceRequestFields,
  ServiceRequestLimits,
  ServiceRequestValidationCodes as Codes,
  ServiceRequestValidationScopes as Scopes,
  categoryIdProblem,
  contactPhoneProblem,
  descriptionProblem,
  isIsoDate,
  preferredDateProblem,
  serviceRequestFieldErrors,
  serviceRequestFieldErrorsFrom,
  serviceRequestMessageKey,
  titleProblem,
  validateServiceRequest,
} from './service-request-rules';

const TODAY = '2026-11-06';
const BAD_REQUEST = 400;
const text = (length: number) => 'x'.repeat(length);

describe('service request field rules', () => {
  it('requires a category token', () => {
    expect(categoryIdProblem(undefined)).toBe(Codes.CategoryRequired);
    expect(categoryIdProblem(null)).toBe(Codes.CategoryRequired);
    expect(categoryIdProblem('  ')).toBe(Codes.CategoryRequired);
    expect(categoryIdProblem('m1_k1_token')).toBeNull();
  });

  it('checks the title: required, then the length bounds inclusive', () => {
    expect(titleProblem(null)).toBe(Codes.TitleRequired);
    expect(titleProblem('   ')).toBe(Codes.TitleRequired);
    expect(titleProblem(text(ServiceRequestLimits.TitleMinLength - 1))).toBe(Codes.TitleTooShort);
    expect(titleProblem(text(ServiceRequestLimits.TitleMinLength))).toBeNull();
    expect(titleProblem(text(ServiceRequestLimits.TitleMaxLength))).toBeNull();
    expect(titleProblem(text(ServiceRequestLimits.TitleMaxLength + 1))).toBe(Codes.TitleTooLong);
  });

  it('checks the description: required, then the length bounds inclusive', () => {
    expect(descriptionProblem(undefined)).toBe(Codes.DescriptionRequired);
    expect(descriptionProblem(text(ServiceRequestLimits.DescriptionMinLength - 1))).toBe(Codes.DescriptionTooShort);
    expect(descriptionProblem(text(ServiceRequestLimits.DescriptionMinLength))).toBeNull();
    expect(descriptionProblem(text(ServiceRequestLimits.DescriptionMaxLength))).toBeNull();
    expect(descriptionProblem(text(ServiceRequestLimits.DescriptionMaxLength + 1))).toBe(Codes.DescriptionTooLong);
  });

  it('checks the contact phone: required, then 05 and 8 digits', () => {
    expect(contactPhoneProblem('')).toBe(Codes.ContactPhoneRequired);
    expect(contactPhoneProblem('\u0030512345678')).toBeNull();
    for (const invalid of ['0612345678', '\u003051234567', '\u00305123456789', '05-2345678', ' \u0030512345678']) {
      expect(contactPhoneProblem(invalid), invalid).toBe(Codes.ContactPhoneInvalid);
    }
    expect(CONTACT_PHONE_PATTERN.source).toBe('^05[0-9]{8}$');
  });

  it('accepts only real YYYY-MM-DD calendar dates', () => {
    expect(isIsoDate('2026-11-06')).toBe(true);
    expect(isIsoDate('2028-02-29')).toBe(true);
    expect(isIsoDate('2026-02-29')).toBe(false);
    expect(isIsoDate('2026-13-01')).toBe(false);
    expect(isIsoDate('2026-11-6')).toBe(false);
    expect(isIsoDate('2026-11-06T00:00:00')).toBe(false);
  });

  it('treats the preferred date as optional, refuses a bad format and a day before today', () => {
    expect(preferredDateProblem(null, TODAY)).toBeNull();
    expect(preferredDateProblem('', TODAY)).toBeNull();
    expect(preferredDateProblem('06/11/2026', TODAY)).toBe(Codes.PreferredDateInvalid);
    expect(preferredDateProblem('2026-11-05', TODAY)).toBe(Codes.PreferredDateInPast);
    expect(preferredDateProblem(TODAY, TODAY)).toBeNull();
    expect(preferredDateProblem('2027-01-01', TODAY)).toBeNull();
  });
});

describe('validateServiceRequest', () => {
  it('validates only the fields of the scope', () => {
    expect(SERVICE_REQUEST_SCOPE_FIELDS[Scopes.Step1]).toEqual(['categoryId', 'title']);
    expect(SERVICE_REQUEST_SCOPE_FIELDS[Scopes.Step2]).toEqual(['description', 'contactPhone', 'preferredDate']);
    expect(SERVICE_REQUEST_SCOPE_FIELDS[Scopes.Submit]).toEqual(Object.values(ServiceRequestFields));
    expect(validateServiceRequest({}, Scopes.Step1, TODAY).errorFields).toEqual(['categoryId', 'title']);
  });

  it('reports one code per failing field and the fields sorted ordinally', () => {
    const result = validateServiceRequest({ title: 'abc', preferredDate: '2026-01-01' }, Scopes.Submit, TODAY);

    expect(result.valid).toBe(false);
    expect(result.errorFields).toEqual(['categoryId', 'contactPhone', 'description', 'preferredDate', 'title']);
    expect(result.errors).toEqual({
      categoryId: Codes.CategoryRequired,
      title: Codes.TitleTooShort,
      description: Codes.DescriptionRequired,
      contactPhone: Codes.ContactPhoneRequired,
      preferredDate: Codes.PreferredDateInPast,
    });
  });

  it('is valid with no errors when every rule holds', () => {
    const result = validateServiceRequest(
      { categoryId: 'm1_k1_x', title: 'Valid title', description: text(25), contactPhone: '\u0030500000000' },
      Scopes.Submit,
      TODAY,
    );

    expect(result).toEqual({ valid: true, errorFields: [], errors: {} });
  });
});

describe('service request messages', () => {
  it('renders the module codes from serviceRequests.errors and any other code from errors', () => {
    expect(serviceRequestMessageKey(Codes.TitleTooShort)).toBe('serviceRequests.errors.SERVICE_REQUESTS_TITLE_TOO_SHORT');
    expect(serviceRequestMessageKey('REQUIRED')).toBe('errors.REQUIRED');
  });

  it('turns a client result into the wizard field-error map', () => {
    const result = validateServiceRequest({ categoryId: 'm1_k1_x', title: 'abc' }, Scopes.Step1, TODAY);

    expect(serviceRequestFieldErrors(result)).toEqual({
      title: ['serviceRequests.errors.SERVICE_REQUESTS_TITLE_TOO_SHORT'],
    });
    expect(serviceRequestFieldErrors(validateServiceRequest({}, Scopes.Step2, TODAY))).toEqual({
      description: ['serviceRequests.errors.SERVICE_REQUESTS_DESCRIPTION_REQUIRED'],
      contactPhone: ['serviceRequests.errors.SERVICE_REQUESTS_CONTACT_PHONE_REQUIRED'],
    });
    expect(
      serviceRequestFieldErrors(validateServiceRequest({ categoryId: 'm1_k1_x', title: 'Valid' }, Scopes.Step1, TODAY)),
    ).toBe(NO_FIELD_ERRORS);
  });

  it('maps a refused submit to field errors, grouping and de-duplicating, keeping ready keys', () => {
    const error = parseApiError({
      status: BAD_REQUEST,
      error: {
        code: 'VALIDATION_FAILED',
        errors: { title: [Codes.TitleTooShort, Codes.TitleTooShort], contactPhone: ['errors.REQUIRED'] },
      },
    });

    expect(serviceRequestFieldErrorsFrom(error)).toEqual({
      title: ['serviceRequests.errors.SERVICE_REQUESTS_TITLE_TOO_SHORT'],
      contactPhone: ['errors.REQUIRED'],
    });
    expect(serviceRequestFieldErrorsFrom(null)).toBe(NO_FIELD_ERRORS);
  });
});
