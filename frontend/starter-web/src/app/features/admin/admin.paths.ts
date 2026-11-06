import { RoutePaths } from '../../core/routing/route-paths';
import { ACCESS_MOUNT_SEGMENT } from './access/access.paths';
import { JOBS_MOUNT_SEGMENT } from './jobs/jobs.paths';
import { LOOKUPS_MOUNT_SEGMENT, LookupsPaths } from './lookups/lookups.paths';
import { PORTAL_CONTENT_MOUNT_SEGMENT, PortalContentPaths } from './portal-content/portal-content.paths';
import { SETTINGS_MOUNT_SEGMENT } from './settings/settings.paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/** The paths of `ADMIN_ROUTES`, relative to the `/admin` mount. */
export const AdminRouteSegments = {
  Home: '',
  Users: 'users',
  Organizations: 'organizations',
  Import: 'import',
  /** The access area (`ACCESS_ROUTES`), owned by `features/admin/access`; the value is its own mount constant. */
  Access: ACCESS_MOUNT_SEGMENT,
  /** The lookups screens (`LOOKUPS_ROUTES`), owned by `features/admin/lookups`. */
  Lookups: LOOKUPS_MOUNT_SEGMENT,
  /** The portal content editor (`PORTAL_CONTENT_ROUTES`), owned by `features/admin/portal-content`. */
  PortalContent: PORTAL_CONTENT_MOUNT_SEGMENT,
  /** The runtime settings screen (`SETTINGS_ROUTES`), owned by `features/admin/settings`. */
  Settings: SETTINGS_MOUNT_SEGMENT,
  /** The background jobs screens (`JOBS_ROUTES`), owned by `features/admin/jobs`. */
  Jobs: JOBS_MOUNT_SEGMENT,
} as const;

function adminLink(...segments: readonly string[]): readonly string[] {
  return [ROOT_SEGMENT, RoutePaths.Admin, ...segments];
}

/**
 * Absolute router commands for the admin screens: `[routerLink]="AdminPaths.users()"` or
 * `router.navigate(AdminPaths.organizations())`. The access screens have their own `AccessPaths`.
 */
export const AdminPaths = {
  home: (): readonly string[] => adminLink(),
  users: (): readonly string[] => adminLink(AdminRouteSegments.Users),
  organizations: (): readonly string[] => adminLink(AdminRouteSegments.Organizations),
  import: (): readonly string[] => adminLink(AdminRouteSegments.Import),
  lookups: LookupsPaths.types,
  portalContent: PortalContentPaths.editor,
  settings: (): readonly string[] => adminLink(AdminRouteSegments.Settings),
  jobs: (): readonly string[] => adminLink(AdminRouteSegments.Jobs),
} as const;
