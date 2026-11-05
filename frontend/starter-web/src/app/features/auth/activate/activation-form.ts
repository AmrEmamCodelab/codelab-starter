import type { ApiFieldError } from '@codelab/ng-security';

/**
 * The shortest password the page lets through. It mirrors the backend's `IdentityLimits.PasswordMinLengthFloor`,
 * the floor every configuration keeps; a deployment may set `Identity:Password:MinLength` higher, and then the
 * server's `PASSWORD_TOO_SHORT` field error shows under the field. Change both sides together.
 */
export const ACTIVATION_PASSWORD_MIN_LENGTH = 8;

/** The longest password, mirroring the backend's `IdentityLimits.PasswordMaxLength`. */
export const ACTIVATION_PASSWORD_MAX_LENGTH = 256;

/** The activation ticket's name in the link's fragment (`/activate#ticket=`). */
export const ACTIVATION_TICKET_PARAM = 'ticket';

/** The request field the server's password errors are keyed by. */
export const ACTIVATION_PASSWORD_FIELD = 'password';

/** The per-field message codes the server answers for a weak password (the backend's `IdentityValidationMessages`). */
export const ActivationServerCodes = {
  PasswordTooShort: 'PASSWORD_TOO_SHORT',
  PasswordTooSimple: 'PASSWORD_TOO_SIMPLE',
} as const;

/** Translation keys of the activation page. */
export const ActivationMessageKeys = {
  Title: 'auth.activate.title',
  Subtitle: 'auth.activate.subtitle',
  PasswordLabel: 'auth.activate.password-label',
  ConfirmLabel: 'auth.activate.confirm-label',
  PasswordHint: 'auth.activate.password-hint',
  Submit: 'auth.activate.submit',
  PasswordRequired: 'auth.activate.password-required',
  PasswordTooShort: 'auth.activate.password-too-short',
  PasswordTooLong: 'auth.activate.password-too-long',
  PasswordTooSimple: 'auth.activate.password-too-simple',
  ConfirmRequired: 'auth.activate.confirm-required',
  ConfirmMismatch: 'auth.activate.confirm-mismatch',
  MissingTicket: 'auth.activate.missing-ticket',
  BackToLogin: 'auth.activate.back-to-login',
} as const;

/** What the activation form submits. */
export interface NewPassword {
  readonly password: string;
  readonly confirm: string;
}

/** One message key per field, or null when the field is fine. */
export interface NewPasswordErrors {
  readonly password: string | null;
  readonly confirm: string | null;
}

/** Client checks before sending: the length bounds of the server policy, and a matching confirmation. */
export function newPasswordErrors(input: NewPassword): NewPasswordErrors {
  return { password: passwordError(input.password), confirm: confirmError(input) };
}

export function hasNewPasswordErrors(errors: NewPasswordErrors): boolean {
  return errors.password !== null || errors.confirm !== null;
}

/** The message for the server's password field error, or null when it sent none this page knows. */
export function serverPasswordErrorKey(fieldErrors: readonly ApiFieldError[]): string | null {
  const codes = fieldErrors
    .filter(error => error.field.toLowerCase() === ACTIVATION_PASSWORD_FIELD)
    .map(error => error.message);
  if (codes.includes(ActivationServerCodes.PasswordTooShort)) return ActivationMessageKeys.PasswordTooShort;
  if (codes.includes(ActivationServerCodes.PasswordTooSimple)) return ActivationMessageKeys.PasswordTooSimple;
  return null;
}

function passwordError(password: string): string | null {
  if (password.length === 0) return ActivationMessageKeys.PasswordRequired;
  if (password.length < ACTIVATION_PASSWORD_MIN_LENGTH) return ActivationMessageKeys.PasswordTooShort;
  if (password.length > ACTIVATION_PASSWORD_MAX_LENGTH) return ActivationMessageKeys.PasswordTooLong;
  return null;
}

function confirmError(input: NewPassword): string | null {
  if (input.confirm.length === 0) return ActivationMessageKeys.ConfirmRequired;
  return input.confirm === input.password ? null : ActivationMessageKeys.ConfirmMismatch;
}
