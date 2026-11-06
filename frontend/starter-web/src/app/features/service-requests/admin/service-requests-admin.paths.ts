import { RoutePaths } from '../../../core/routing/route-paths';

/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

/**
 * The segment `SERVICE_REQUESTS_ADMIN_ROUTES` hangs on inside the admin area: `/admin/requests` (spread into
 * `ADMIN_ROUTES`).
 */
export const SERVICE_REQUESTS_ADMIN_MOUNT = 'requests';

/** Route parameter names of the admin screens (bound to component inputs of the same name). */
export const ServiceRequestsAdminRouteParams = {
  Id: 'id',
} as const;

/** The child paths of the admin mount. */
export const ServiceRequestsAdminRouteSegments = {
  List: '',
  Detail: `:${ServiceRequestsAdminRouteParams.Id}`,
} as const;

function adminRequestsLink(...segments: readonly string[]): readonly string[] {
  return [ROOT_SEGMENT, RoutePaths.Admin, SERVICE_REQUESTS_ADMIN_MOUNT, ...segments];
}

/** Absolute router commands of the admin oversight screens. */
export const ServiceRequestsAdminPaths = {
  list: (): readonly string[] => adminRequestsLink(),
  detail: (id: string): readonly string[] => adminRequestsLink(id),
} as const;
