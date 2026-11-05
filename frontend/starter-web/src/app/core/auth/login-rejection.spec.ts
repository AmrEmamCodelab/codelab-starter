import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import {
  IdentityErrorCodes,
  LOGIN_TRANSLATED_CODES,
  LoginMessageKeys,
  loginRejectionFrom,
  ssoErrorRejection,
  ssoExchangeRejectionFrom,
} from './login-rejection';

function refusal(status: number, code?: string, correlationId?: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: { code, correlationId } });
}

describe('loginRejectionFrom', () => {
  it('shows the generic sentence without a reference code for refused credentials', () => {
    const rejection = loginRejectionFrom(
      refusal(HttpStatus.badRequest, IdentityErrorCodes.InvalidCredentials, 'trace-1'),
    );
    expect(rejection).toEqual({ messageKey: LoginMessageKeys.InvalidCredentials, referenceCode: null });
  });

  it('shows a locked-out account exactly like a wrong password (same server code)', () => {
    const wrongPassword = loginRejectionFrom(refusal(HttpStatus.badRequest, IdentityErrorCodes.InvalidCredentials));
    const lockedOut = loginRejectionFrom(refusal(HttpStatus.badRequest, IdentityErrorCodes.InvalidCredentials));
    expect(lockedOut).toEqual(wrongPassword);
  });

  it('shows a disabled sign-in method from its own key, with no reference code', () => {
    const rejection = loginRejectionFrom(
      refusal(HttpStatus.notFound, IdentityErrorCodes.LoginModeDisabled, 'trace-2'),
    );
    expect(rejection).toEqual({ messageKey: `errors.${IdentityErrorCodes.LoginModeDisabled}`, referenceCode: null });
  });

  it('shows a refused activation ticket from its own key', () => {
    expect(loginRejectionFrom(refusal(HttpStatus.badRequest, IdentityErrorCodes.ActivationRejected)).messageKey).toBe(
      `errors.${IdentityErrorCodes.ActivationRejected}`,
    );
  });

  it('keeps the reference code of a server fault', () => {
    const rejection = loginRejectionFrom(refusal(HttpStatus.serverError, ApiErrorCodes.internalError, 'abcdef0123456789'));
    expect(rejection.messageKey).toBe(`errors.${ApiErrorCodes.internalError}`);
    expect(rejection.referenceCode).not.toBeNull();
  });

  it('falls back to the internal error for an unknown code', () => {
    expect(loginRejectionFrom(refusal(HttpStatus.badRequest, 'SOMETHING_NEW')).messageKey).toBe(
      `errors.${ApiErrorCodes.internalError}`,
    );
  });

  it('reads a non-HTTP failure as a server fault', () => {
    expect(loginRejectionFrom(new Error('signing')).messageKey).toBe(`errors.${ApiErrorCodes.internalError}`);
  });

  it('translates every platform code and the Identity codes it renders', () => {
    expect(LOGIN_TRANSLATED_CODES.has(ApiErrorCodes.validation)).toBe(true);
    expect(LOGIN_TRANSLATED_CODES.has(IdentityErrorCodes.SsoTicketRejected)).toBe(true);
    expect(LOGIN_TRANSLATED_CODES.has(IdentityErrorCodes.InvalidCredentials)).toBe(false);
  });
});

describe('ssoErrorRejection', () => {
  it('maps a known callback code to its message', () => {
    expect(ssoErrorRejection(IdentityErrorCodes.SsoUnavailable).messageKey).toBe(
      `errors.${IdentityErrorCodes.SsoUnavailable}`,
    );
  });

  it('reads an account refusal as "no matching account"', () => {
    expect(ssoErrorRejection(IdentityErrorCodes.InvalidCredentials).messageKey).toBe(LoginMessageKeys.SsoAccountRefused);
  });

  it.each([null, '', 'javascript:alert(1)', 'UNKNOWN'])('collapses %s to the generic SSO failure', code => {
    expect(ssoErrorRejection(code)).toEqual({
      messageKey: `errors.${IdentityErrorCodes.SsoFailed}`,
      referenceCode: null,
    });
  });
});

describe('ssoExchangeRejectionFrom', () => {
  it('reads a refused account as "no matching account"', () => {
    expect(
      ssoExchangeRejectionFrom(refusal(HttpStatus.badRequest, IdentityErrorCodes.InvalidCredentials)).messageKey,
    ).toBe(LoginMessageKeys.SsoAccountRefused);
  });

  it('reads a replayed ticket as a spent sign-in', () => {
    expect(
      ssoExchangeRejectionFrom(refusal(HttpStatus.badRequest, IdentityErrorCodes.SsoTicketRejected)).messageKey,
    ).toBe(`errors.${IdentityErrorCodes.SsoTicketRejected}`);
  });
});
