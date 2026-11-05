import { describe, expect, it } from 'vitest';
import { APP_NAV_ITEMS } from './app-nav';
import { PermissionKeys, type PermissionKey } from './core/permissions/permission-keys.generated';
import { RoutePaths } from './core/routing/route-paths';
import { AccessPaths } from './features/admin/access/access.paths';
import { AdminPaths } from './features/admin/admin.paths';
import { visibleNavItems } from './layouts/app-shell/shell-nav';

function holding(...keys: PermissionKey[]): (key: PermissionKey) => boolean {
  const held = new Set<string>(keys);
  return key => held.has(key);
}

describe('the staff navigation', () => {
  it('gates every entry by generated permission keys', () => {
    const known = new Set<string>(Object.values(PermissionKeys));
    for (const item of APP_NAV_ITEMS) {
      expect(item.targets.length).toBeGreaterThan(0);
      for (const target of item.targets) {
        expect(known.has(target.permission)).toBe(true);
      }
    }
  });

  it('offers the dashboard alone to a dashboard.view holder', () => {
    expect(visibleNavItems(APP_NAV_ITEMS, holding(PermissionKeys.DashboardView)).map(item => item.path)).toEqual([RoutePaths.Dashboard]);
  });

  it('leads a holder of access.admin.view to the admin home', () => {
    const visible = visibleNavItems(APP_NAV_ITEMS, holding(PermissionKeys.AccessAdminView, PermissionKeys.UsersView));
    expect(visible.map(item => item.link)).toEqual([AdminPaths.home()]);
  });

  it.each([
    [PermissionKeys.UsersView, AdminPaths.users()],
    [PermissionKeys.OrganizationsView, AdminPaths.organizations()],
    [PermissionKeys.UsersImport, AdminPaths.import()],
    [PermissionKeys.AccessRolesView, AccessPaths.roles()],
  ])('offers the admin entry to a holder of only %s, leading to that page', (key, link) => {
    const visible = visibleNavItems(APP_NAV_ITEMS, holding(key));
    expect(visible.map(item => item.path)).toEqual([RoutePaths.Admin]);
    expect(visible[0]?.link).toEqual(link);
  });
});
