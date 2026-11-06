import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { ADMIN_TILES } from './admin-page/admin-tiles';
import { AdminPaths } from './admin.paths';

/** One way into the admin area: the page a holder of `permission` lands on. */
export interface AdminEntry {
  readonly permission: PermissionKey;
  readonly link: readonly string[];
}

/**
 * The ways into the admin area: `access.admin.view` and the key of every admin home tile, each leading to the admin
 * home. The home opens to a holder of any of these keys and lists only the tiles they hold, so a role granted only
 * `lookups.view` and `settings.view` reaches both pages from it without also needing `access.admin.view`.
 */
export const ADMIN_ENTRIES: readonly AdminEntry[] = [
  { permission: PermissionKeys.AccessAdminView, link: AdminPaths.home() },
  ...ADMIN_TILES.map((tile) => ({ permission: tile.permission, link: AdminPaths.home() })),
];

/**
 * Every key that opens a page of the admin area: the `/admin` mount and the shell's admin entry are offered to a
 * holder of any one of them. Each page under the mount is still gated by its own key.
 */
export const ADMIN_AREA_PERMISSIONS: readonly PermissionKey[] = ADMIN_ENTRIES.map((entry) => entry.permission);

/** Where the admin entry leads a holder of `has`: the admin home when they hold any admin key, else null. */
export function adminEntryLink(has: (key: PermissionKey) => boolean): readonly string[] | null {
  return ADMIN_ENTRIES.find((entry) => has(entry.permission))?.link ?? null;
}
