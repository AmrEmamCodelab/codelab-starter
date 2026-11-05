import { describe, expect, it } from 'vitest';
import {
  ACTIVATION_PASSWORD_MAX_LENGTH,
  ACTIVATION_PASSWORD_MIN_LENGTH,
  ActivationMessageKeys,
  ActivationServerCodes,
  hasNewPasswordErrors,
  newPasswordErrors,
  serverPasswordErrorKey,
} from './activation-form';

const LONG_ENOUGH = 'a'.repeat(ACTIVATION_PASSWORD_MIN_LENGTH);

describe('newPasswordErrors', () => {
  it('accepts a password of the minimum length that is confirmed', () => {
    const errors = newPasswordErrors({ password: LONG_ENOUGH, confirm: LONG_ENOUGH });
    expect(errors).toEqual({ password: null, confirm: null });
    expect(hasNewPasswordErrors(errors)).toBe(false);
  });

  it('requires both fields', () => {
    expect(newPasswordErrors({ password: '', confirm: '' })).toEqual({
      password: ActivationMessageKeys.PasswordRequired,
      confirm: ActivationMessageKeys.ConfirmRequired,
    });
  });

  it('refuses a password one character short of the minimum', () => {
    const short = LONG_ENOUGH.slice(1);
    expect(newPasswordErrors({ password: short, confirm: short }).password).toBe(ActivationMessageKeys.PasswordTooShort);
  });

  it('refuses a password over the maximum', () => {
    const long = 'a'.repeat(ACTIVATION_PASSWORD_MAX_LENGTH + 1);
    expect(newPasswordErrors({ password: long, confirm: long }).password).toBe(ActivationMessageKeys.PasswordTooLong);
  });

  it('refuses a confirmation that differs', () => {
    const errors = newPasswordErrors({ password: LONG_ENOUGH, confirm: `${LONG_ENOUGH}x` });
    expect(errors.confirm).toBe(ActivationMessageKeys.ConfirmMismatch);
    expect(hasNewPasswordErrors(errors)).toBe(true);
  });

  it('mirrors the server floor', () => {
    expect(ACTIVATION_PASSWORD_MIN_LENGTH).toBe(8);
  });
});

describe('serverPasswordErrorKey', () => {
  it('maps the server policy codes on the password field', () => {
    expect(serverPasswordErrorKey([{ field: 'Password', message: ActivationServerCodes.PasswordTooShort }])).toBe(
      ActivationMessageKeys.PasswordTooShort,
    );
    expect(serverPasswordErrorKey([{ field: 'password', message: ActivationServerCodes.PasswordTooSimple }])).toBe(
      ActivationMessageKeys.PasswordTooSimple,
    );
  });

  it('ignores other fields and unknown codes', () => {
    expect(serverPasswordErrorKey([{ field: 'ticket', message: ActivationServerCodes.PasswordTooShort }])).toBeNull();
    expect(serverPasswordErrorKey([{ field: 'password', message: 'REQUIRED' }])).toBeNull();
    expect(serverPasswordErrorKey([])).toBeNull();
  });
});
