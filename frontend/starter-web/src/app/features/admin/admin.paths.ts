import { RoutePaths } from '../../core/routing/route-paths';
import { ACCESS_MOUNT_SEGMENT } from './access/access.paths';

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
} as const;
