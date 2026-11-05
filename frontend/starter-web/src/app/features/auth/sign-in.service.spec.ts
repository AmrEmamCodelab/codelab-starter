import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { CoreTranslationKeys } from '../../core/i18n/core-translation-keys';
import { fakeAuth, type FakeAuth } from '../../layouts/layout.testing';
import { SignInErrorCodes, SignInMessageKeys, SignInService, signInFailureFrom } from './sign-in.service';

const CREDENTIALS = { userName: '  admin ', password: 'secret' };
const CORRELATION_ID = '5c1e9b0d2f7a4e3c8b6d1a0f9e8d7c6b';

function httpError(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

/** The login endpoint's real answer to a wrong password. */
const INVALID_CREDENTIALS = httpError(HttpStatus.badRequest, { code: SignInErrorCodes.InvalidCredentials });

describe('signInFailureFrom', () => {
  it('reads the real wrong-password answer (400 IDENTITY_INVALID_CREDENTIALS) as wrong credentials', () => {
    // The body POST /api/auth/login actually sends (SignInUseCases: Refusal.Invalid(InvalidCredentials)).
    const realAnswer = httpError(HttpStatus.badRequest, {
      code: SignInErrorCodes.InvalidCredentials,
      correlationId: CORRELATION_ID,
      referenceCode: 'BCAFA-E1736',
    });
    expect(signInFailureFrom(realAnswer)).toEqual({
      messageKey: SignInMessageKeys.InvalidCredentials,
      referenceCode: null,
    });
  });

  it('branches on the server code, not the status: a bare 401 is not read as wrong credentials', () => {
    const failure = signInFailureFrom(httpError(HttpStatus.unauthorized, { code: ApiErrorCodes.unauthenticated }));
    expect(failure.messageKey).toBe(CoreTranslationKeys.Errors[ApiErrorCodes.unauthenticated]);
  });

  it('reads other refusals from their problem+json code', () => {
    const failure = signInFailureFrom(httpError(HttpStatus.badRequest, { code: ApiErrorCodes.validation }));
    expect(failure.messageKey).toBe(CoreTranslationKeys.Errors[ApiErrorCodes.validation]);
  });

  it('keeps the reference code of a server error', () => {
    const failure = signInFailureFrom(
      httpError(HttpStatus.serverError, { code: ApiErrorCodes.internalError, correlationId: CORRELATION_ID }),
    );
    expect(failure.messageKey).toBe(CoreTranslationKeys.Errors[ApiErrorCodes.internalError]);
    expect(failure.referenceCode).toEqual(expect.any(String));
  });

  it('shows an unreachable server as a network failure', () => {
    expect(signInFailureFrom(httpError(HttpStatus.noResponse)).messageKey).toBe(
      CoreTranslationKeys.Errors[ApiErrorCodes.offline],
    );
  });

  it('treats a failure that is not an HTTP response as an internal error', () => {
    expect(signInFailureFrom(new Error('signing failed')).messageKey).toBe(
      CoreTranslationKeys.Errors[ApiErrorCodes.internalError],
    );
  });
});

describe('SignInService', () => {
  let auth: FakeAuth;
  let service: SignInService;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    auth = fakeAuth(null);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), SignInService, { provide: AuthService, useValue: auth }],
    });
    service = TestBed.inject(SignInService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  it('signs in with the trimmed user name and goes to the safe return URL', async () => {
    await expect(service.signIn(CREDENTIALS, '/admin')).resolves.toBe(true);
    expect(auth.login).toHaveBeenCalledWith('admin', 'secret');
    expect(navigate).toHaveBeenCalledWith('/admin');
    expect(service.failure()).toBeNull();
    expect(service.busy()).toBe(false);
  });

  it('goes to the dashboard when the return URL is missing or off-site', async () => {
    await service.signIn(CREDENTIALS, 'https://elsewhere.example');
    expect(String(navigate.mock.calls[0]?.[0])).toBe('/');
  });

  it('shows the refusal and stays on the page', async () => {
    auth.login.mockRejectedValueOnce(INVALID_CREDENTIALS);
    await expect(service.signIn(CREDENTIALS, null)).resolves.toBe(false);
    expect(service.failure()?.messageKey).toBe(SignInMessageKeys.InvalidCredentials);
    expect(navigate).not.toHaveBeenCalled();
    expect(service.busy()).toBe(false);
  });

  it('clears the previous refusal on the next attempt', async () => {
    auth.login.mockRejectedValueOnce(INVALID_CREDENTIALS);
    await service.signIn(CREDENTIALS, null);
    await service.signIn(CREDENTIALS, null);
    expect(service.failure()).toBeNull();
  });

  it('ignores a second submit while the first is still pending', async () => {
    let finish: () => void = () => undefined;
    auth.login.mockReturnValueOnce(new Promise<void>(resolve => (finish = resolve)));
    const first = service.signIn(CREDENTIALS, null);
    expect(service.busy()).toBe(true);
    await expect(service.signIn(CREDENTIALS, null)).resolves.toBe(false);
    finish();
    await expect(first).resolves.toBe(true);
    expect(auth.login).toHaveBeenCalledTimes(1);
  });
});
