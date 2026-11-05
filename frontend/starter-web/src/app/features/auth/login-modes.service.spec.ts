import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthClient } from '../../core/api/clients/auth.client';
import { SignInMode } from '../../core/auth/sign-in-mode';
import { LoginModesService, LoginModesStates } from './login-modes.service';

describe('LoginModesService', () => {
  let modes: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    modes = vi.fn().mockReturnValue(of({ basic: true, activeDirectory: false, sso: true }));
    TestBed.configureTestingModule({ providers: [LoginModesService, { provide: AuthClient, useValue: { modes } }] });
  });

  it('starts loading with no methods', () => {
    const service = TestBed.inject(LoginModesService);
    expect(service.state()).toBe(LoginModesStates.Loading);
    expect(service.methods()).toEqual([]);
  });

  it('loads the enabled methods', async () => {
    const service = TestBed.inject(LoginModesService);
    await service.load();
    expect(service.state()).toBe(LoginModesStates.Ready);
    expect(service.methods()).toEqual([SignInMode.Basic, SignInMode.Sso]);
    expect(service.hasChoice()).toBe(true);
  });

  it('offers no choice for a single method', async () => {
    modes.mockReturnValue(of({ basic: false, activeDirectory: true, sso: false }));
    const service = TestBed.inject(LoginModesService);
    await service.load();
    expect(service.hasChoice()).toBe(false);
  });

  it('shows a failed load with its reference code, and recovers on retry', async () => {
    modes.mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: HttpStatus.serverError,
            error: { code: ApiErrorCodes.internalError, correlationId: 'abcdef0123456789' },
          }),
      ),
    );
    const service = TestBed.inject(LoginModesService);
    await service.load();
    expect(service.state()).toBe(LoginModesStates.Failed);
    expect(service.methods()).toEqual([]);
    expect(service.failure()?.referenceCode).not.toBeNull();

    modes.mockReturnValue(of({ basic: true, activeDirectory: false, sso: false }));
    await service.load();
    expect(service.state()).toBe(LoginModesStates.Ready);
    expect(service.failure()).toBeNull();
  });
});
