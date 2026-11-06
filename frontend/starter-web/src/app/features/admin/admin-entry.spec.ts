import { describe, expect, it } from 'vitest';
import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { ADMIN_TILES } from './admin-page/admin-tiles';
import { ADMIN_AREA_PERMISSIONS, ADMIN_ENTRIES, adminEntryLink } from './admin-entry';
import { AdminPaths } from './admin.paths';

function holding(...keys: PermissionKey[]): (key: PermissionKey) => boolean {
  const held = new Set<string>(keys);
  return (key) => held.has(key);
}

describe('admin entry', () => {
  it('opens the area to access.admin.view and to the key of every admin home tile', () => {
    expect(ADMIN_AREA_PERMISSIONS).toEqual([PermissionKeys.AccessAdminView, ...ADMIN_TILES.map((tile) => tile.permission)]);
    expect(ADMIN_AREA_PERMISSIONS).toEqual(expect.arrayContaining([PermissionKeys.UsersView, PermissionKeys.OrganizationsView, PermissionKeys.UsersImport]));
  });

  it('prefers the admin home for a holder of access.admin.view', () => {
    expect(adminEntryLink(holding(PermissionKeys.UsersImport, PermissionKeys.AccessAdminView))).toEqual(AdminPaths.home());
  });

  it('leads a holder of only page keys to the admin home, which lists each of their tiles', () => {
    expect(adminEntryLink(holding(PermissionKeys.UsersView))).toEqual(AdminPaths.home());
    expect(
      adminEntryLink(holding(PermissionKeys.JobsView, PermissionKeys.LookupsView, PermissionKeys.PortalContentView, PermissionKeys.SettingsView)),
    ).toEqual(AdminPaths.home());
  });

  it('leads every admin key to the admin home', () => {
    expect(ADMIN_ENTRIES.map((entry) => entry.link)).toEqual(ADMIN_ENTRIES.map(() => AdminPaths.home()));
  });

  it('leads nowhere without an admin key', () => {
    expect(adminEntryLink(holding(PermissionKeys.DashboardView))).toBeNull();
    expect(ADMIN_ENTRIES.every((entry) => entry.link.length > 0)).toBe(true);
  });
});
