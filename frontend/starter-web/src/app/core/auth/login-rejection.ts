import { HttpErrorResponse } from '@angular/common/http';
import {
  ApiErrorCodes,
  ERROR_MESSAGE_KEY_PREFIX,
  HttpStatus,
  apiErrorMessageKey,
  parseApiError,
  type ApiError,
} from '@codelab/ng-security';

/**
 * The problem+json codes the Identity endpoints answer on their own. They mirror the backend's
 * `IdentityErrorCodes` (backend/src/Modules/Identity/Application/IdentityErrorCodes.cs); the contract is the
 * code, never the HTTP status (a wrong password is a 400, a 401 would mean an expired session). Change both
 * sides together.
 */
export const IdentityErrorCodes = {
  /**
   * The credentials do not match an active account. ONE code for every reason: wrong password, unknown user,
   * inactive or locked-out account, or an account bound to another sign-in method (no enumeration).
   */
  InvalidCredentials: 'IDENTITY_INVALID_CREDENTIALS',
  /** The sign-in method is switched off in this deployment (`Auth:Modes`); the endpoint answers 404. */
  LoginModeDisabled: 'IDENTITY_LOGIN_MODE_DISABLED',
  /** The activation ticket is unknown, spent or expired. */
  ActivationRejected: 'IDENTITY_ACTIVATION_REJECTED',
  /** The SSO round trip failed (state, code exchange or id_token validation); the generic SSO refusal. */
  SsoFailed: 'IDENTITY_SSO_FAILED',
  /** The one-time SSO exchange ticket is unknown, spent or expired (a replay lands here). */
  SsoTicketRejected: 'IDENTITY_SSO_TICKET_REJECTED',
  /** The identity provider could not be reached. */
  SsoUnavailable: 'IDENTITY_SSO_UNAVAILABLE',
  /** The identity provider or the sign-in state refused the round trip (the backend's `#error=` fragment code). */
  SsoRejected: 'IDENTITY_SSO_REJECTED',
  /** The Active Directory could not be reached, so no password was checked; the endpoint answers 503. */
  DirectoryUnavailable: 'IDENTITY_DIRECTORY_UNAVAILABLE',
} as const;

export type IdentityErrorCode = (typeof IdentityErrorCodes)[keyof typeof IdentityErrorCodes];

/** Translation keys the sign-in flows show on their own (the rest are `errors.<CODE>`). */
export const LoginMessageKeys = {
  /** A refused user name and password. It never says which of the two was wrong, nor that an account is locked. */
  InvalidCredentials: 'auth.login.invalid-credentials',
  /** The identity provider signed the user in, but no active account here matches (or it is bound elsewhere). */
  SsoAccountRefused: 'auth.sso.account-refused',
} as const;

/** What a sign-in page shows after a refusal. */
export interface LoginRejection {
  readonly messageKey: string;
  /** The short code the user reads out to support; present for outages and server faults only. */
  readonly referenceCode: string | null;
}

/** The Identity codes that have an `errors.<CODE>` entry in both bundles. */
const IDENTITY_TRANSLATED_CODES: readonly string[] = [
  IdentityErrorCodes.LoginModeDisabled,
  IdentityErrorCodes.ActivationRejected,
  IdentityErrorCodes.SsoFailed,
  IdentityErrorCodes.SsoTicketRejected,
  IdentityErrorCodes.SsoUnavailable,
  IdentityErrorCodes.SsoRejected,
  IdentityErrorCodes.DirectoryUnavailable,
];

/** Outages: the user can only retry later, and support traces them by reference code. */
const OUTAGE_CODES: readonly string[] = [IdentityErrorCodes.SsoUnavailable, IdentityErrorCodes.DirectoryUnavailable];

/** Every code the sign-in flows can render as `errors.<CODE>`: the platform codes plus the Identity ones. */
export const LOGIN_TRANSLATED_CODES: ReadonlySet<string> = new Set([
  ...Object.values(ApiErrorCodes),
  ...IDENTITY_TRANSLATED_CODES,
]);

/** Refusals that are the user's situation, not a fault: nothing for support to trace, so no reference code. */
const USER_REFUSALS: ReadonlySet<string> = new Set([
  ...IDENTITY_TRANSLATED_CODES.filter(code => !OUTAGE_CODES.includes(code)),
  ApiErrorCodes.validation,
]);

/** An unexpected failure that is not an HTTP response (for example a request-signing error). */
const NON_HTTP_FAILURE = { status: HttpStatus.serverError } as const;

/** Parses whatever a sign-in call threw through the ng-security problem+json parser. */
export function parseLoginError(error: unknown): ApiError {
  return parseApiError(error instanceof HttpErrorResponse ? error : NON_HTTP_FAILURE);
}

/**
 * The message for a refused basic, Active Directory, SSO-exchange or activation call. A lockout answers the same
 * `IDENTITY_INVALID_CREDENTIALS` as a wrong password, so it shows the same generic sentence.
 */
export function loginRejectionFrom(error: unknown): LoginRejection {
  return rejectionFor(parseLoginError(error));
}

/** {@link loginRejectionFrom} over an already parsed error. */
export function rejectionFor(apiError: ApiError): LoginRejection {
  if (apiError.serverCode === IdentityErrorCodes.InvalidCredentials) {
    return { messageKey: LoginMessageKeys.InvalidCredentials, referenceCode: null };
  }
  const referenceCode = USER_REFUSALS.has(apiError.code) ? null : (apiError.referenceCode ?? null);
  return { messageKey: apiErrorMessageKey(apiError, LOGIN_TRANSLATED_CODES), referenceCode };
}

/**
 * The message for the `#error=<code>` the API's SSO callback redirect carries. The code came through the address
 * bar, so it is only ever matched against known codes: anything else is the generic SSO failure.
 */
export function ssoErrorRejection(code: string | null): LoginRejection {
  if (code === IdentityErrorCodes.InvalidCredentials) {
    return { messageKey: LoginMessageKeys.SsoAccountRefused, referenceCode: null };
  }
  const known = code !== null && IDENTITY_TRANSLATED_CODES.includes(code) ? code : IdentityErrorCodes.SsoFailed;
  return { messageKey: `${ERROR_MESSAGE_KEY_PREFIX}${known}`, referenceCode: null };
}

/**
 * The message for a refused `POST api/auth/sso/exchange`: an account refusal reads as "no matching account here"
 * (the user typed no password, so "wrong password" would mislead); anything else as {@link loginRejectionFrom}.
 */
export function ssoExchangeRejectionFrom(error: unknown): LoginRejection {
  const apiError = parseLoginError(error);
  return apiError.serverCode === IdentityErrorCodes.InvalidCredentials
    ? ssoErrorRejection(IdentityErrorCodes.InvalidCredentials)
    : rejectionFor(apiError);
}
