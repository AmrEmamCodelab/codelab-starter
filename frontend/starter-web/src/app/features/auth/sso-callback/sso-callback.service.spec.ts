import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../../../core/auth/auth.service';
import { IdentityErrorCodes, LoginMessageKeys } from '../../../core/auth/login-rejection';
import { BrowserLocation } from '../../../core/session/browser-location';
import { SsoCallbackService, SsoCallbackStates } from './sso-callback.service';

describe('SsoCallbackService', () => {
  let auth: {
    completeSsoSignIn: ReturnType<typeof vi.fn>;
    takeSsoReturnUrl: ReturnType<typeof vi.fn>;
    takeSsoBindingProof: ReturnType<typeof vi.fn>;
  };
  let browser: { fragment: ReturnType<typeof vi.fn>; clearFragment: ReturnType<typeof vi.fn> };
  let navigate: ReturnType<typeof vi.spyOn>;

  function setUp(fragment: string): SsoCallbackService {
    browser.fragment.mockReturnValue(fragment);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        SsoCallbackService,
        { provide: AuthService, useValue: auth },
        { provide: BrowserLocation, useValue: browser },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    return TestBed.inject(SsoCallbackService);
  }

  beforeEach(() => {
    auth = {
      completeSsoSignIn: vi.fn().mockResolvedValue(undefined),
      takeSsoReturnUrl: vi.fn().mockReturnValue(null),
      takeSsoBindingProof: vi.fn().mockReturnValue('proof-1'),
    };
    browser = { fragment: vi.fn(), clearFragment: vi.fn() };
  });

  it('swaps the ticket, clears the fragment first and lands on the dashboard', async () => {
    const callback = setUp('ticket=t-1');
    await callback.complete();
    expect(browser.clearFragment).toHaveBeenCalled();
    expect(browser.clearFragment.mock.invocationCallOrder[0]).toBeLessThan(
      auth.completeSsoSignIn.mock.invocationCallOrder[0] ?? 0,
    );
    expect(auth.completeSsoSignIn).toHaveBeenCalledWith('t-1', 'proof-1');
    expect(String(navigate.mock.calls[0]?.[0])).toBe('/');
    expect(navigate.mock.calls[0]?.[1]).toEqual({ replaceUrl: true });
    expect(callback.state()).toBe(SsoCallbackStates.Working);
  });

  it('refuses a ticket this tab did not start (no binding secret) without calling the API', async () => {
    auth.takeSsoBindingProof.mockReturnValue(null);
    const callback = setUp('ticket=t-1');
    await callback.complete();
    expect(browser.clearFragment).toHaveBeenCalled();
    expect(auth.completeSsoSignIn).not.toHaveBeenCalled();
    expect(callback.state()).toBe(SsoCallbackStates.Failed);
    expect(callback.failure()?.messageKey).toBe(`errors.${IdentityErrorCodes.SsoFailed}`);
  });

  it('lands on the return URL the fragment carries', async () => {
    await setUp(`ticket=t-1&returnUrl=${encodeURIComponent('/admin/users?page=2')}`).complete();
    expect(navigate).toHaveBeenCalledWith('/admin/users?page=2', { replaceUrl: true });
  });

  it('lands on the return URL remembered at the SSO start', async () => {
    auth.takeSsoReturnUrl.mockReturnValue('/admin');
    await setUp('ticket=t-1').complete();
    expect(navigate).toHaveBeenCalledWith('/admin', { replaceUrl: true });
  });

  it('ignores an off-site return URL in the fragment', async () => {
    await setUp(`ticket=t-1&returnUrl=${encodeURIComponent('https://evil.test/')}`).complete();
    expect(String(navigate.mock.calls[0]?.[0])).toBe('/');
  });

  it('shows the refusal the API sent in the fragment, without exchanging', async () => {
    const callback = setUp(`error=${IdentityErrorCodes.SsoUnavailable}`);
    await callback.complete();
    expect(auth.completeSsoSignIn).not.toHaveBeenCalled();
    expect(browser.clearFragment).toHaveBeenCalled();
    expect(callback.state()).toBe(SsoCallbackStates.Failed);
    expect(callback.failure()?.messageKey).toBe(`errors.${IdentityErrorCodes.SsoUnavailable}`);
  });

  it('shows the generic failure when the fragment has no ticket', async () => {
    const callback = setUp('');
    await callback.complete();
    expect(callback.failure()?.messageKey).toBe(`errors.${IdentityErrorCodes.SsoFailed}`);
  });

  it('shows a replayed ticket as a spent sign-in', async () => {
    auth.completeSsoSignIn.mockRejectedValue(
      new HttpErrorResponse({ status: HttpStatus.badRequest, error: { code: IdentityErrorCodes.SsoTicketRejected } }),
    );
    const callback = setUp('ticket=spent');
    await callback.complete();
    expect(callback.state()).toBe(SsoCallbackStates.Failed);
    expect(callback.failure()?.messageKey).toBe(`errors.${IdentityErrorCodes.SsoTicketRejected}`);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('shows an unmatched account as such', async () => {
    auth.completeSsoSignIn.mockRejectedValue(
      new HttpErrorResponse({ status: HttpStatus.badRequest, error: { code: IdentityErrorCodes.InvalidCredentials } }),
    );
    const callback = setUp('ticket=t-1');
    await callback.complete();
    expect(callback.failure()?.messageKey).toBe(LoginMessageKeys.SsoAccountRefused);
  });

  it('exchanges the ticket only once', async () => {
    const callback = setUp('ticket=t-1');
    await Promise.all([callback.complete(), callback.complete()]);
    await callback.complete();
    expect(auth.completeSsoSignIn).toHaveBeenCalledTimes(1);
    expect(browser.fragment).toHaveBeenCalledTimes(1);
  });

  it('goes back to the login page keeping the deep link', async () => {
    const callback = setUp(`error=x&returnUrl=${encodeURIComponent('/admin')}`);
    await callback.complete();
    await callback.backToLogin();
    expect(String(navigate.mock.calls[0]?.[0])).toBe('/login?returnUrl=%2Fadmin');
    expect(navigate.mock.calls[0]?.[1]).toEqual({ replaceUrl: true });
  });
});
