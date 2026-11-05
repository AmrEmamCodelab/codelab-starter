import { RoutePaths } from '../../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/**
 * The segment the admin routes mount `ACCESS_ROUTES` on (`/admin/access`). `AdminPaths` in `admin.paths.ts` uses
 * this same value for its `access` child, so the two never drift.
 */
export const ACCESS_MOUNT_SEGMENT = 'access';

/** Route parameter names of the access screens. */
export const AccessRouteParams = {
  RoleId: 'roleId',
  UserId: 'userId',
} as const;

/** The paths of `ACCESS_ROUTES`, relative to the mount. */
export const AccessRouteSegments = {
  Roles: 'roles',
  RoleDetail: `roles/:${AccessRouteParams.RoleId}`,
  UserGrants: `users/:${AccessRouteParams.UserId}/grants`,
} as const;

const ROLES_SEGMENT = AccessRouteSegments.Roles;
const USERS_SEGMENT = 'users';
const GRANTS_SEGMENT = 'grants';

function accessLink(...segments: readonly string[]): readonly string[] {
  return [ROOT_SEGMENT, RoutePaths.Admin, ACCESS_MOUNT_SEGMENT, ...segments];
}

/**
 * Absolute router commands for the access screens: `[routerLink]="AccessPaths.roles()"` or
 * `router.navigate(AccessPaths.userGrants(user.id))`. Ids are PublicIds (the users list's id opens the grants page).
 */
export const AccessPaths = {
  roles: (): readonly string[] => accessLink(ROLES_SEGMENT),
  roleDetail: (roleId: string): readonly string[] => accessLink(ROLES_SEGMENT, roleId),
  userGrants: (userId: string): readonly string[] => accessLink(USERS_SEGMENT, userId, GRANTS_SEGMENT),
} as const;
