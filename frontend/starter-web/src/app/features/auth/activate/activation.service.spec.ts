import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthClient } from '../../../core/api/clients/auth.client';
import { IdentityErrorCodes } from '../../../core/auth/login-rejection';
import { LoginNotices, RoutePaths, RouteQueryParams } from '../../../core/routing/route-paths';
import { BrowserLocation } from '../../../core/session/browser-location';
import { ActivationMessageKeys, ActivationServerCodes } from './activation-form';
import { ActivationService } from './activation.service';

describe('ActivationService', () => {
  let activate: ReturnType<typeof vi.fn>;
  let browser: { fragment: ReturnType<typeof vi.fn>; clearFragment: ReturnType<typeof vi.fn> };
  let navigate: ReturnType<typeof vi.spyOn>;

  function setUp(fragment: string): ActivationService {
    browser.fragment.mockReturnValue(fragment);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ActivationService,
        { provide: AuthClient, useValue: { activate } },
        { provide: BrowserLocation, useValue: browser },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const service = TestBed.inject(ActivationService);
    service.init();
    return service;
  }

  beforeEach(() => {
    activate = vi.fn().mockReturnValue(of(null));
    browser = { fragment: vi.fn(), clearFragment: vi.fn() };
  });

  it('takes the ticket from the fragment and clears it', () => {
    const service = setUp('ticket=act-1');
    expect(service.hasTicket()).toBe(true);
    expect(browser.clearFragment).toHaveBeenCalled();
  });

  it.each(['', 'ticket=', 'ticket=%20', 'other=1'])('has no ticket for the fragment %j', fragment => {
    expect(setUp(fragment).hasTicket()).toBe(false);
  });

  it('sets the password and goes to the login page with the notice', async () => {
    const service = setUp('ticket=act-1');
    await expect(service.activate('a-new-password')).resolves.toBe(true);
    expect(activate).toHaveBeenCalledWith({ ticket: 'act-1', password: 'a-new-password' });
    expect(navigate).toHaveBeenCalledWith([RoutePaths.Login], {
      queryParams: { [RouteQueryParams.Notice]: LoginNotices.Activated },
      replaceUrl: true,
    });
  });

  it('does not send without a ticket', async () => {
    const service = setUp('');
    await expect(service.activate('a-new-password')).resolves.toBe(false);
    expect(activate).not.toHaveBeenCalled();
  });

  it('shows a refused ticket without a reference code', async () => {
    activate.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: HttpStatus.badRequest,
            error: { code: IdentityErrorCodes.ActivationRejected, correlationId: 'abcdef0123456789' },
          }),
      ),
    );
    const service = setUp('ticket=spent');
    await expect(service.activate('a-new-password')).resolves.toBe(false);
    expect(service.failure()).toEqual({
      messageKey: `errors.${IdentityErrorCodes.ActivationRejected}`,
      referenceCode: null,
    });
    expect(service.busy()).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('shows a stricter server policy under the password field', async () => {
    activate.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: HttpStatus.badRequest,
            error: { code: ApiErrorCodes.validation, errors: { password: [ActivationServerCodes.PasswordTooSimple] } },
          }),
      ),
    );
    const service = setUp('ticket=act-1');
    await service.activate('aaaaaaaa');
    expect(service.passwordError()).toBe(ActivationMessageKeys.PasswordTooSimple);
    expect(service.failure()).toBeNull();
  });

  it('ignores a second submit while the first is pending, and sends a spent ticket only once', async () => {
    const service = setUp('ticket=act-1');
    const first = service.activate('a-new-password');
    await expect(service.activate('a-new-password')).resolves.toBe(false);
    await first;
    await expect(service.activate('a-new-password')).resolves.toBe(false);
    expect(activate).toHaveBeenCalledTimes(1);
  });
});
