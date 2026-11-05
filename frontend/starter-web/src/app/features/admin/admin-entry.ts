import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { ADMIN_TILES } from './admin-page/admin-tiles';
import { AdminPaths } from './admin.paths';

/** One way into the admin area: the page a holder of `permission` lands on. */
export interface AdminEntry {
  readonly permission: PermissionKey;
  readonly link: readonly string[];
}

/**
 * The ways into the admin area, in order of preference: the admin home for a holder of `access.admin.view`, else
 * the first admin page whose own key the user holds (the same pages, in the same order, as the home's tiles). So a
 * role granted only `users.view` reaches the users page without also needing `access.admin.view`.
 */
export const ADMIN_ENTRIES: readonly AdminEntry[] = [
  { permission: PermissionKeys.AccessAdminView, link: AdminPaths.home() },
  ...ADMIN_TILES.map((tile) => ({ permission: tile.permission, link: tile.link })),
];

/**
 * Every key that opens a page of the admin area: the `/admin` mount and the shell's admin entry are offered to a
 * holder of any one of them. Each page under the mount is still gated by its own key.
 */
export const ADMIN_AREA_PERMISSIONS: readonly PermissionKey[] = ADMIN_ENTRIES.map((entry) => entry.permission);

/** Where the admin entry leads a holder of `has`: the first entry whose key they hold, or null when none. */
export function adminEntryLink(has: (key: PermissionKey) => boolean): readonly string[] | null {
  return ADMIN_ENTRIES.find((entry) => has(entry.permission))?.link ?? null;
}
