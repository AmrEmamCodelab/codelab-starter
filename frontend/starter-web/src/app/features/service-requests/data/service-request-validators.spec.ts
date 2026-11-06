import { FormControl } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import { ServiceRequestValidationCodes as Codes } from './service-request-rules';
import { serviceRequestControlMessageKey, serviceRequestValidators } from './service-request-validators';

const TODAY = '2026-11-06';

describe('serviceRequestValidators', () => {
  it('passes valid values and fails each field with its one code', () => {
    expect(new FormControl('m1_k1_x', serviceRequestValidators.categoryId).errors).toBeNull();
    expect(new FormControl(null, serviceRequestValidators.categoryId).errors).toEqual({ [Codes.CategoryRequired]: true });

    expect(new FormControl('Valid title', serviceRequestValidators.title).errors).toBeNull();
    expect(new FormControl('abc', serviceRequestValidators.title).errors).toEqual({ [Codes.TitleTooShort]: true });

    expect(new FormControl('d'.repeat(20), serviceRequestValidators.description).errors).toBeNull();
    expect(new FormControl('', serviceRequestValidators.description).errors).toEqual({ [Codes.DescriptionRequired]: true });

    expect(new FormControl('\u0030512345678', serviceRequestValidators.contactPhone).errors).toBeNull();
    expect(new FormControl('0712345678', serviceRequestValidators.contactPhone).errors).toEqual({
      [Codes.ContactPhoneInvalid]: true,
    });
  });

  it('treats a non-text control value as missing', () => {
    expect(new FormControl(42, serviceRequestValidators.title).errors).toEqual({ [Codes.TitleRequired]: true });
  });

  it('reads today on every check for the preferred date', () => {
    let today = TODAY;
    const control = new FormControl('2026-11-06', serviceRequestValidators.preferredDate(() => today));
    expect(control.errors).toBeNull();

    today = '2026-11-07';
    control.updateValueAndValidity();
    expect(control.errors).toEqual({ [Codes.PreferredDateInPast]: true });

    control.setValue(null);
    expect(control.errors).toBeNull();
    control.setValue('2026-02-30');
    expect(control.errors).toEqual({ [Codes.PreferredDateInvalid]: true });
  });
});

describe('serviceRequestControlMessageKey', () => {
  it('gives the message key of the control code, or null', () => {
    expect(serviceRequestControlMessageKey({ [Codes.TitleTooLong]: true })).toBe(
      'serviceRequests.errors.SERVICE_REQUESTS_TITLE_TOO_LONG',
    );
    expect(serviceRequestControlMessageKey(null)).toBeNull();
    expect(serviceRequestControlMessageKey(undefined)).toBeNull();
    expect(serviceRequestControlMessageKey({ required: true })).toBeNull();
  });
});
