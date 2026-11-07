import type { DgaIconName } from '@codelab/ng-dga';
import { PermissionKeys, type PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { AccessPaths } from '../access/access.paths';
import { AdminPaths } from '../admin.paths';

/** One tile of the admin home: an area of the admin area, shown only to a holder of its key. */
export interface AdminTile {
  readonly id: string;
  readonly icon: DgaIconName;
  readonly titleKey: string;
  readonly textKey: string;
  readonly link: readonly string[];
  readonly permission: PermissionKey;
}

/** The admin home's tiles in display order; each one is gated by the key of the page it opens. */
export const ADMIN_TILES: readonly AdminTile[] = [
  {
    id: 'users',
    icon: 'user',
    titleKey: 'admin.home.users.title',
    textKey: 'admin.home.users.text',
    link: AdminPaths.users(),
    permission: PermissionKeys.UsersView,
  },
  {
    id: 'organizations',
    icon: 'home',
    titleKey: 'admin.home.organizations.title',
    textKey: 'admin.home.organizations.text',
    link: AdminPaths.organizations(),
    permission: PermissionKeys.OrganizationsView,
  },
  {
    id: 'import',
    icon: 'arrow-up',
    titleKey: 'admin.home.import.title',
    textKey: 'admin.home.import.text',
    link: AdminPaths.import(),
    permission: PermissionKeys.UsersImport,
  },
  {
    id: 'roles',
    icon: 'lock',
    titleKey: 'admin.home.roles.title',
    textKey: 'admin.home.roles.text',
    link: AccessPaths.roles(),
    permission: PermissionKeys.AccessRolesView,
  },
  {
    id: 'lookups',
    icon: 'menu',
    titleKey: 'admin.home.lookups.title',
    textKey: 'admin.home.lookups.text',
    link: AdminPaths.lookups(),
    permission: PermissionKeys.LookupsView,
  },
  // codelab:module-begin Portal
  {
    id: 'portal-content',
    icon: 'file',
    titleKey: 'admin.home.portal-content.title',
    textKey: 'admin.home.portal-content.text',
    link: AdminPaths.portalContent(),
    permission: PermissionKeys.PortalContentView,
  },
  // codelab:module-end Portal
  {
    id: 'settings',
    icon: 'filter',
    titleKey: 'admin.home.settings.title',
    textKey: 'admin.home.settings.text',
    link: AdminPaths.settings(),
    permission: PermissionKeys.SettingsView,
  },
  {
    id: 'jobs',
    icon: 'clock',
    titleKey: 'admin.home.jobs.title',
    textKey: 'admin.home.jobs.text',
    link: AdminPaths.jobs(),
    permission: PermissionKeys.JobsView,
  },
];

/** The tiles a holder of `has` sees. */
export function visibleTiles(has: (key: PermissionKey) => boolean, tiles: readonly AdminTile[] = ADMIN_TILES): readonly AdminTile[] {
  return tiles.filter((tile) => has(tile.permission));
}
