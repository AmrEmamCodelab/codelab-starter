import { parseApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { NO_FIELD_ERRORS, allFieldErrors, fieldErrorsFrom, fieldMessageKey, firstFieldError, hasFieldErrors } from './field-errors';

const BAD_REQUEST = 400;

function validationError(errors: Record<string, unknown>) {
  return parseApiError({ status: BAD_REQUEST, error: { code: 'VALIDATION_FAILED', errors } });
}

describe('field errors', () => {
  it('turns message codes into errors.<CODE> keys and keeps existing keys', () => {
    expect(fieldMessageKey('USER_NAME_REQUIRED')).toBe('errors.USER_NAME_REQUIRED');
    expect(fieldMessageKey('errors.ALREADY_A_KEY')).toBe('errors.ALREADY_A_KEY');
  });

  it('groups a problem+json errors dictionary by field, once per message', () => {
    const map = fieldErrorsFrom(validationError({
      userName: ['USER_NAME_REQUIRED', 'USER_NAME_REQUIRED', 'USER_NAME_TOO_LONG'],
      '$.email': 'EMAIL_INVALID',
    }));
    expect(map).toEqual({
      userName: ['errors.USER_NAME_REQUIRED', 'errors.USER_NAME_TOO_LONG'],
      email: ['errors.EMAIL_INVALID'],
    });
    expect(firstFieldError(map, 'userName')).toBe('errors.USER_NAME_REQUIRED');
    expect(firstFieldError(map, 'displayName')).toBeNull();
    expect(hasFieldErrors(map)).toBe(true);
    expect(allFieldErrors(map)).toEqual([
      { field: 'userName', key: 'errors.USER_NAME_REQUIRED' },
      { field: 'userName', key: 'errors.USER_NAME_TOO_LONG' },
      { field: 'email', key: 'errors.EMAIL_INVALID' },
    ]);
  });

  it('is empty without an error or field errors', () => {
    expect(fieldErrorsFrom(null)).toBe(NO_FIELD_ERRORS);
    expect(fieldErrorsFrom(undefined)).toBe(NO_FIELD_ERRORS);
    expect(fieldErrorsFrom(parseApiError({ status: 0 }))).toBe(NO_FIELD_ERRORS);
    expect(hasFieldErrors(NO_FIELD_ERRORS)).toBe(false);
    expect(hasFieldErrors({ userName: [] })).toBe(false);
  });
});
