import type { Route } from '@angular/router';
import { assertRoutesGated, findUngatedRoutes } from '@codelab/ng-tools/routes';
import { describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../core/routing/route-paths';
import { ACCESS_MOUNT_SEGMENT } from './access/access.paths';
import { ACCESS_ROUTES } from './access/access.routes';
import { AdminPageComponent } from './admin-page/admin-page.component';
import { AdminPaths, AdminRouteSegments } from './admin.paths';
import { ADMIN_ROUTES } from './admin.routes';
import { UserImportPageComponent } from './import/user-import-page.component';
import { OrganizationsPageComponent } from './organizations/organizations-page.component';
import { UsersPageComponent } from './users/users-page.component';

const GATE_OPTIONS = {
  permissionDataKey: RouteDataKeys.Permission,
  lazyChildren: { [AdminRouteSegments.Access]: ACCESS_ROUTES },
  knownPermissions: Object.values(PermissionKeys),
};

function routeAt(path: string): Route {
  const route = ADMIN_ROUTES.find((candidate) => candidate.path === path);
  if (!route) {
    throw new Error(`No admin route '${path}'.`);
  }
  return route;
}

async function loadedComponent(route: Route): Promise<unknown> {
  return (route.loadComponent as () => Promise<unknown>)();
}

describe('ADMIN_ROUTES', () => {
  it('gates every page, including the lazily loaded access screens, by a known key', () => {
    expect(findUngatedRoutes(ADMIN_ROUTES, GATE_OPTIONS)).toEqual([]);
    expect(() => assertRoutesGated(ADMIN_ROUTES, GATE_OPTIONS)).not.toThrow();
  });

  it.each([
    [AdminRouteSegments.Home, PermissionKeys.AccessAdminView, AdminPageComponent],
    [AdminRouteSegments.Users, PermissionKeys.UsersView, UsersPageComponent],
    [AdminRouteSegments.Organizations, PermissionKeys.OrganizationsView, OrganizationsPageComponent],
    [AdminRouteSegments.Import, PermissionKeys.UsersImport, UserImportPageComponent],
  ])("'%s' requires %s through permissionGuard() and opens its page", async (path, permission, component) => {
    const route = routeAt(path);
    expect(route.data?.[RouteDataKeys.Permission]).toBe(permission);
    expect(route.canActivate).toHaveLength(1);
    expect(await loadedComponent(route)).toBe(component);
  });

  it('matches the home page only on the exact mount path', () => {
    expect(routeAt(AdminRouteSegments.Home).pathMatch).toBe('full');
  });

  it('mounts the access screens on their own segment', async () => {
    const route = routeAt(AdminRouteSegments.Access);
    expect(AdminRouteSegments.Access).toBe(ACCESS_MOUNT_SEGMENT);
    expect(await (route.loadChildren as () => Promise<unknown>)()).toBe(ACCESS_ROUTES);
  });

  it('builds absolute links under /admin', () => {
    expect(AdminPaths.home()).toEqual(['/', 'admin']);
    expect(AdminPaths.users()).toEqual(['/', 'admin', 'users']);
    expect(AdminPaths.organizations()).toEqual(['/', 'admin', 'organizations']);
    expect(AdminPaths.import()).toEqual(['/', 'admin', 'import']);
  });
});
