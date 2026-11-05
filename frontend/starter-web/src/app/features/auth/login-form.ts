import type { AuthModesResponse } from '../../core/api/clients/api.models';
import { SignInMode } from '../../core/auth/sign-in-mode';
import { CoreTranslationKeys } from '../../core/i18n/core-translation-keys';
import { LoginNotices, SessionEndReasons, type SessionEndReason } from '../../core/routing/route-paths';

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

/** Translation keys of the login page's method chooser. */
export const LoginTextKeys = {
  MethodsLabel: 'auth.login.methods-label',
  ActivatedNotice: 'auth.login.activated-notice',
  ModesLoading: 'auth.login.modes-loading',
  ModesFailed: 'auth.login.modes-failed',
  ModesRetry: 'auth.login.modes-retry',
  NoMethods: 'auth.login.no-methods',
} as const;

/** Per sign-in method: its tab label and the card's subtitle while it is selected. */
export const LoginMethodTextKeys: Readonly<Record<SignInMode, { readonly tab: string; readonly subtitle: string }>> = {
  [SignInMode.Basic]: { tab: 'auth.login.method-basic', subtitle: 'auth.login.subtitle' },
  [SignInMode.ActiveDirectory]: { tab: 'auth.login.method-ad', subtitle: 'auth.login.ad-subtitle' },
  [SignInMode.Sso]: { tab: 'auth.login.method-sso', subtitle: 'auth.login.sso-subtitle' },
};

/** The enabled sign-in methods, always in the same order: basic, Active Directory, SSO. */
export function loginMethodsFrom(modes: AuthModesResponse): readonly SignInMode[] {
  const methods: SignInMode[] = [];
  if (modes.basic === true) methods.push(SignInMode.Basic);
  if (modes.activeDirectory === true) methods.push(SignInMode.ActiveDirectory);
  if (modes.sso === true) methods.push(SignInMode.Sso);
  return methods;
}

/** True for the methods that take a user name and password (basic and Active Directory). */
export function usesPasswordForm(method: SignInMode): boolean {
  return method !== SignInMode.Sso;
}

/** The success notice for `?notice=` (after an account activation), or null for none or an unknown notice. */
export function loginNoticeKey(notice: string | null | undefined): string | null {
  return notice === LoginNotices.Activated ? LoginTextKeys.ActivatedNotice : null;
}
