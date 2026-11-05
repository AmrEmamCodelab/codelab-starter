// The /admin mount over the real route tables: a role granted one admin page key (users.view) reaches that page
// without also holding access.admin.view, and a user with no admin key is still refused at the mount.
import { TestBed } from '@angular/core/testing';
import { provideRouter, type ActivatedRouteSnapshot, type CanActivateFn, type Route, type RouterStateSnapshot } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SHELL_ROUTE } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { PermissionKeys, type PermissionKey } from './core/permissions/permission-keys.generated';
import { PermissionStore } from './core/permissions/permission.store';
import { PermissionSyncService } from './core/permissions/permission-sync.service';
import { RoutePaths } from './core/routing/route-paths';
import { AdminRouteSegments } from './features/admin/admin.paths';
import { ADMIN_ROUTES } from './features/admin/admin.routes';

const STATE = { url: '/admin/users' } as RouterStateSnapshot;
const UNAUTHORIZED = '/unauthorized';

function childAt(routes: readonly Route[] | undefined, path: string): Route {
  const route = routes?.find((candidate) => candidate.path === path);
  if (!route) {
    throw new Error(`No route '${path}'.`);
  }
  return route;
}

describe('the /admin mount', () => {
  let store: PermissionStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isAuthenticated: () => true } },
        { provide: PermissionSyncService, useValue: { ensureLoaded: vi.fn().mockResolvedValue(true), lastFailure: () => null } },
      ],
    });
    store = TestBed.inject(PermissionStore);
  });

  async function passes(route: Route, held: readonly PermissionKey[]): Promise<boolean> {
    store.set([...held]);
    const guards = (route.canActivate ?? []) as CanActivateFn[];
    const snapshot = { data: route.data ?? {} } as unknown as ActivatedRouteSnapshot;
    for (const guard of guards) {
      const result: unknown = await TestBed.runInInjectionContext(() => guard(snapshot, STATE));
      if (result !== true) {
        expect(String(result)).toBe(UNAUTHORIZED);
        return false;
      }
    }
    return guards.length > 0;
  }

  const mount = childAt(SHELL_ROUTE.children, RoutePaths.Admin);

  it('lets a users.view-only user through the mount and onto the users page', async () => {
    await expect(passes(mount, [PermissionKeys.UsersView])).resolves.toBe(true);
    await expect(passes(childAt(ADMIN_ROUTES, AdminRouteSegments.Users), [PermissionKeys.UsersView])).resolves.toBe(true);
  });

  it.each([PermissionKeys.OrganizationsView, PermissionKeys.UsersImport, PermissionKeys.AccessRolesView, PermissionKeys.AccessAdminView])(
    'lets a holder of only %s through the mount',
    async (key) => {
      await expect(passes(mount, [key])).resolves.toBe(true);
    },
  );

  it('still refuses a users.view-only user the pages of other keys', async () => {
    await expect(passes(childAt(ADMIN_ROUTES, AdminRouteSegments.Home), [PermissionKeys.UsersView])).resolves.toBe(false);
    await expect(passes(childAt(ADMIN_ROUTES, AdminRouteSegments.Import), [PermissionKeys.UsersView])).resolves.toBe(false);
  });

  it('refuses a user with no admin key at the mount', async () => {
    await expect(passes(mount, [PermissionKeys.DashboardView])).resolves.toBe(false);
  });
});
