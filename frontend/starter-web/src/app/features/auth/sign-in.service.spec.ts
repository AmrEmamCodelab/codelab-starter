import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { IdentityErrorCodes, loginRejectionFrom } from '../../core/auth/login-rejection';
import { SignInMode } from '../../core/auth/sign-in-mode';
import { CoreTranslationKeys } from '../../core/i18n/core-translation-keys';
import { SignInMessageKeys, SignInService, SsoStartFailure } from './sign-in.service';

const CREDENTIALS = { userName: '  admin ', password: 'secret' };
const CORRELATION_ID = '5c1e9b0d2f7a4e3c8b6d1a0f9e8d7c6b';

function httpError(status: number, error: unknown = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

/** The login endpoint's real answer to a wrong password (and to a locked-out account). */
const INVALID_CREDENTIALS = httpError(HttpStatus.badRequest, { code: IdentityErrorCodes.InvalidCredentials });

describe('loginRejectionFrom (as the login page uses it)', () => {
  it('reads the real wrong-password answer (400 IDENTITY_INVALID_CREDENTIALS) as wrong credentials', () => {
    const realAnswer = httpError(HttpStatus.badRequest, {
      code: IdentityErrorCodes.InvalidCredentials,
      correlationId: CORRELATION_ID,
      referenceCode: 'BCAFA-E1736',
    });
    expect(loginRejectionFrom(realAnswer)).toEqual({
      messageKey: SignInMessageKeys.InvalidCredentials,
      referenceCode: null,
    });
  });

  it('branches on the server code, not the status: a bare 401 is not read as wrong credentials', () => {
    const failure = loginRejectionFrom(httpError(HttpStatus.unauthorized, { code: ApiErrorCodes.unauthenticated }));
    expect(failure.messageKey).toBe(CoreTranslationKeys.Errors[ApiErrorCodes.unauthenticated]);
  });

  it('keeps the reference code of a server error', () => {
    const failure = loginRejectionFrom(
      httpError(HttpStatus.serverError, { code: ApiErrorCodes.internalError, correlationId: CORRELATION_ID }),
    );
    expect(failure.messageKey).toBe(CoreTranslationKeys.Errors[ApiErrorCodes.internalError]);
    expect(failure.referenceCode).toEqual(expect.any(String));
  });

  it('shows an unreachable server as a network failure', () => {
    expect(loginRejectionFrom(httpError(HttpStatus.noResponse)).messageKey).toBe(
      CoreTranslationKeys.Errors[ApiErrorCodes.offline],
    );
  });
});

describe('SignInService', () => {
  let auth: {
    login: ReturnType<typeof vi.fn>;
    loginWithDirectory: ReturnType<typeof vi.fn>;
    startSsoSignIn: ReturnType<typeof vi.fn>;
  };
  let service: SignInService;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    auth = {
      login: vi.fn().mockResolvedValue(undefined),
      loginWithDirectory: vi.fn().mockResolvedValue(undefined),
      startSsoSignIn: vi.fn().mockResolvedValue(true),
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), SignInService, { provide: AuthService, useValue: auth }],
    });
    service = TestBed.inject(SignInService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  it('signs in with the trimmed user name and goes to the safe return URL', async () => {
    await expect(service.signIn(CREDENTIALS, SignInMode.Basic, '/admin')).resolves.toBe(true);
    expect(auth.login).toHaveBeenCalledWith('admin', 'secret');
    expect(auth.loginWithDirectory).not.toHaveBeenCalled();
    expect(navigate).toHaveBeenCalledWith('/admin');
    expect(service.failure()).toBeNull();
    expect(service.busy()).toBe(false);
  });

  it('signs in against Active Directory when that method is chosen', async () => {
    await expect(service.signIn(CREDENTIALS, SignInMode.ActiveDirectory, null)).resolves.toBe(true);
    expect(auth.loginWithDirectory).toHaveBeenCalledWith('admin', 'secret');
    expect(auth.login).not.toHaveBeenCalled();
  });

  it('goes to the dashboard when the return URL is missing or off-site', async () => {
    await service.signIn(CREDENTIALS, SignInMode.Basic, 'https://elsewhere.example');
    expect(String(navigate.mock.calls[0]?.[0])).toBe('/');
  });

  it('shows the refusal and stays on the page', async () => {
    auth.loginWithDirectory.mockRejectedValueOnce(INVALID_CREDENTIALS);
    await expect(service.signIn(CREDENTIALS, SignInMode.ActiveDirectory, null)).resolves.toBe(false);
    expect(service.failure()?.messageKey).toBe(SignInMessageKeys.InvalidCredentials);
    expect(navigate).not.toHaveBeenCalled();
    expect(service.busy()).toBe(false);
  });

  it('shows a disabled method from its own message', async () => {
    auth.login.mockRejectedValueOnce(httpError(HttpStatus.notFound, { code: IdentityErrorCodes.LoginModeDisabled }));
    await service.signIn(CREDENTIALS, SignInMode.Basic, null);
    expect(service.failure()?.messageKey).toBe(`errors.${IdentityErrorCodes.LoginModeDisabled}`);
  });

  it('clears the previous refusal on the next attempt', async () => {
    auth.login.mockRejectedValueOnce(INVALID_CREDENTIALS);
    await service.signIn(CREDENTIALS, SignInMode.Basic, null);
    await service.signIn(CREDENTIALS, SignInMode.Basic, null);
    expect(service.failure()).toBeNull();
  });

  it('ignores a second submit while the first is still pending', async () => {
    let finish: () => void = () => undefined;
    auth.login.mockReturnValueOnce(new Promise<void>(resolve => (finish = resolve)));
    const first = service.signIn(CREDENTIALS, SignInMode.Basic, null);
    expect(service.busy()).toBe(true);
    await expect(service.signIn(CREDENTIALS, SignInMode.Basic, null)).resolves.toBe(false);
    finish();
    await expect(first).resolves.toBe(true);
    expect(auth.login).toHaveBeenCalledTimes(1);
  });

  it('starts SSO with the return URL and stays busy while the browser leaves', async () => {
    const first = service.startSso('/admin');
    expect(service.busy()).toBe(true);
    await service.startSso('/admin');
    await first;
    expect(auth.startSsoSignIn).toHaveBeenCalledWith('/admin');
    expect(service.busy()).toBe(true);
    expect(auth.startSsoSignIn).toHaveBeenCalledTimes(1);
  });

  it('shows a refused SSO navigation', async () => {
    auth.startSsoSignIn.mockResolvedValue(false);
    await service.startSso(null);
    expect(service.failure()).toEqual(SsoStartFailure);
    expect(service.busy()).toBe(false);
  });

  it('forgets the refusal when asked', async () => {
    auth.login.mockRejectedValueOnce(INVALID_CREDENTIALS);
    await service.signIn(CREDENTIALS, SignInMode.Basic, null);
    service.clearFailure();
    expect(service.failure()).toBeNull();
  });
});
