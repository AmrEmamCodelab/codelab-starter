import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  provideRouter,
  type ActivatedRouteSnapshot,
  type RouterStateSnapshot,
  type UrlTree,
} from '@angular/router';
import { ApiErrorCodes, HttpStatus, type ApiError } from '@codelab/ng-security';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorDialogStore } from '../http/error-dialog.store';
import { PermissionStore } from '../permissions/permission.store';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { permissionGuard } from './permission.guard';

const STATE = { url: '/admin' } as RouterStateSnapshot;

function route(data: Record<string, unknown> = {}): ActivatedRouteSnapshot {
  return { data } as unknown as ActivatedRouteSnapshot;
}

describe('core guards', () => {
  const authenticated = signal(true);
  let ensureLoaded: ReturnType<typeof vi.fn>;
  let lastFailure: ReturnType<typeof vi.fn>;
  let store: PermissionStore;

  beforeEach(() => {
    authenticated.set(true);
    ensureLoaded = vi.fn().mockResolvedValue(true);
    lastFailure = vi.fn().mockReturnValue(null);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAuthenticated: authenticated } },
        { provide: PermissionSyncService, useValue: { ensureLoaded, lastFailure } },
      ],
    });
    store = TestBed.inject(PermissionStore);
  });

  const run = (guard: typeof authGuard, snapshot = route()) =>
    TestBed.runInInjectionContext(() => guard(snapshot, STATE));

  describe('authGuard', () => {
    it('lets a signed-in user through', () => {
      expect(run(authGuard)).toBe(true);
    });

    it('sends a signed-out user to login with the return URL', () => {
      authenticated.set(false);
      expect(String(run(authGuard) as UrlTree)).toBe('/login?returnUrl=%2Fadmin');
    });
  });

  describe('permissionGuard', () => {
    it('allows a held key passed as the argument', async () => {
      store.set(['access.admin.view']);
      await expect(run(permissionGuard('access.admin.view'))).resolves.toBe(true);
    });

    it('reads the key from route data', async () => {
      store.set(['dashboard.view']);
      await expect(run(permissionGuard(), route({ permission: 'dashboard.view' }))).resolves.toBe(true);
    });

    it('lets a holder of any one key in a list through', async () => {
      store.set(['users.view']);
      await expect(run(permissionGuard(['access.admin.view', 'users.view']))).resolves.toBe(true);
    });

    it('sends a user holding none of the listed keys to the 403 page', async () => {
      store.set(['dashboard.view']);
      expect(String(await run(permissionGuard(['access.admin.view', 'users.view'])))).toBe('/unauthorized');
    });

    it('fails closed on an empty key list', async () => {
      store.set(['dashboard.view']);
      expect(String(await run(permissionGuard([]), route({ permission: 'dashboard.view' })))).toBe('/unauthorized');
    });

    it('sends a user without the key to the 403 page', async () => {
      store.set(['dashboard.view']);
      const result = (await run(permissionGuard('access.admin.view'))) as UrlTree;
      expect(String(result)).toBe('/unauthorized');
    });

    it('fails closed when the route names no permission', async () => {
      store.set(['dashboard.view']);
      expect(String(await run(permissionGuard()))).toBe('/unauthorized');
    });

    it('fails closed when the permissions cannot be loaded and no failure is known', async () => {
      ensureLoaded.mockResolvedValue(false);
      store.set(['dashboard.view']);
      expect(String(await run(permissionGuard('dashboard.view')))).toBe('/unauthorized');
    });

    it('shows a failed sync (server 500) in the error dialog with its reference code, not as a 403', async () => {
      const serverFault: ApiError = {
        status: HttpStatus.serverError,
        code: ApiErrorCodes.internalError,
        serverCode: ApiErrorCodes.internalError,
        correlationId: 'abcde12345',
        referenceCode: 'ABCDE-12345',
        fieldErrors: [],
      };
      ensureLoaded.mockResolvedValue(false);
      lastFailure.mockReturnValue(serverFault);
      store.set(['access.admin.view']);
      await expect(run(permissionGuard('access.admin.view'))).resolves.toBe(false);
      const dialog = TestBed.inject(ErrorDialogStore);
      expect(dialog.current()).toBe(serverFault);
      expect(dialog.referenceCode()).toBe('ABCDE-12345');
    });

    it('leaves a 401 during the sync to the session-expiry handling, without the dialog', async () => {
      ensureLoaded.mockResolvedValue(false);
      lastFailure.mockReturnValue({ status: HttpStatus.unauthorized, code: ApiErrorCodes.unauthenticated, fieldErrors: [] });
      await expect(run(permissionGuard('dashboard.view'))).resolves.toBe(false);
      expect(TestBed.inject(ErrorDialogStore).current()).toBeNull();
    });

    it('still sends a missing permission to the 403 page after a good sync', async () => {
      store.set(['dashboard.view']);
      expect(String(await run(permissionGuard('access.admin.view')))).toBe('/unauthorized');
      expect(TestBed.inject(ErrorDialogStore).current()).toBeNull();
    });

    it('sends a signed-out user to login', async () => {
      authenticated.set(false);
      expect(String(await run(permissionGuard('dashboard.view')))).toBe('/login?returnUrl=%2Fadmin');
      expect(ensureLoaded).not.toHaveBeenCalled();
    });
  });
});
