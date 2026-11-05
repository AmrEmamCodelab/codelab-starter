import type { Route } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { ACCESS_MOUNT_SEGMENT, AccessPaths, AccessRouteSegments } from './access.paths';
import { ACCESS_ROUTES } from './access.routes';
import { unsavedChangesGuard, type LeaveAware } from './common/unsaved-changes.guard';

function routeAt(path: string): Route {
  const route = ACCESS_ROUTES.find((candidate) => candidate.path === path);
  if (!route) {
    throw new Error(`no route ${path}`);
  }
  return route;
}

describe('ACCESS_ROUTES', () => {
  it('names a permission on every route', () => {
    for (const route of ACCESS_ROUTES) {
      expect(route.data?.[RouteDataKeys.Permission], route.path).toBeTruthy();
    }
  });

  it('gates every page with the permission guard', () => {
    const pages = ACCESS_ROUTES.filter((route) => route.loadComponent);
    expect(pages).toHaveLength(3);
    for (const route of pages) {
      expect(route.canActivate, route.path).toHaveLength(1);
    }
  });

  it('gates the roles list and the role editor with access.roles.view', () => {
    expect(routeAt(AccessRouteSegments.Roles).data?.[RouteDataKeys.Permission]).toBe(PermissionKeys.AccessRolesView);
    expect(routeAt(AccessRouteSegments.RoleDetail).data?.[RouteDataKeys.Permission]).toBe(PermissionKeys.AccessRolesView);
  });

  it('gates the user grants page with access.grants.view', () => {
    expect(routeAt(AccessRouteSegments.UserGrants).data?.[RouteDataKeys.Permission]).toBe(PermissionKeys.AccessGrantsView);
  });

  it('guards unsaved edits on both editors', () => {
    expect(routeAt(AccessRouteSegments.RoleDetail).canDeactivate).toEqual([unsavedChangesGuard]);
    expect(routeAt(AccessRouteSegments.UserGrants).canDeactivate).toEqual([unsavedChangesGuard]);
  });

  it('opens the roles list on the bare mount', () => {
    expect(routeAt('').redirectTo).toBe(AccessRouteSegments.Roles);
  });

  it('loads each page component', async () => {
    for (const route of ACCESS_ROUTES.filter((candidate) => candidate.loadComponent)) {
      const component = await route.loadComponent!();
      expect(component, route.path).toBeTruthy();
    }
  });
});

describe('AccessPaths', () => {
  it('builds absolute links under /admin/access', () => {
    expect(AccessPaths.roles()).toEqual(['/', 'admin', ACCESS_MOUNT_SEGMENT, 'roles']);
    expect(AccessPaths.roleDetail('r1')).toEqual(['/', 'admin', ACCESS_MOUNT_SEGMENT, 'roles', 'r1']);
    expect(AccessPaths.userGrants('u1')).toEqual(['/', 'admin', ACCESS_MOUNT_SEGMENT, 'users', 'u1', 'grants']);
  });

  it('matches the route segments', () => {
    expect(AccessRouteSegments.RoleDetail).toBe('roles/:roleId');
    expect(AccessRouteSegments.UserGrants).toBe('users/:userId/grants');
  });
});

describe('unsavedChangesGuard', () => {
  const run = (page: LeaveAware | null) =>
    (unsavedChangesGuard as (page: LeaveAware | null) => boolean | Promise<boolean>)(page);

  it('asks the page', async () => {
    expect(run({ canLeave: () => true })).toBe(true);
    await expect(run({ canLeave: () => Promise.resolve(false) })).resolves.toBe(false);
  });

  it('lets a missing page go', () => {
    expect(run(null)).toBe(true);
  });
});
