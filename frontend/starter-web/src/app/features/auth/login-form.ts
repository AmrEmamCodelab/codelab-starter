import { CoreTranslationKeys } from '../../core/i18n/core-translation-keys';
import { SessionEndReasons, type SessionEndReason } from '../../core/routing/route-paths';

/** What the login form submits. */
export interface Credentials {
  readonly userName: string;
  readonly password: string;
}

/** Translation keys of the login form's own field messages. */
export const LoginFieldMessageKeys = {
  UserNameRequired: 'auth.login.user-name-required',
  PasswordRequired: 'auth.login.password-required',
} as const;

/** One message key per field, or null when the field is fine. */
export interface CredentialErrors {
  readonly userName: string | null;
  readonly password: string | null;
}

/** Client-side checks before the form is sent: both fields are required (a blank user name counts as empty). */
export function credentialErrors(credentials: Credentials): CredentialErrors {
  return {
    userName: credentials.userName.trim() === '' ? LoginFieldMessageKeys.UserNameRequired : null,
    password: credentials.password.length === 0 ? LoginFieldMessageKeys.PasswordRequired : null,
  };
}

export function hasCredentialErrors(errors: CredentialErrors): boolean {
  return errors.userName !== null || errors.password !== null;
}

const SESSION_END_REASONS: ReadonlySet<string> = new Set(Object.values(SessionEndReasons));

function isSessionEndReason(value: string | null | undefined): value is SessionEndReason {
  return typeof value === 'string' && SESSION_END_REASONS.has(value);
}

/** The notice for `?reason=` (why the previous session ended), or null for none or an unknown reason. */
export function sessionEndedNoticeKey(reason: string | null | undefined): string | null {
  return isSessionEndReason(reason) ? CoreTranslationKeys.SessionEnded[reason] : null;
}
