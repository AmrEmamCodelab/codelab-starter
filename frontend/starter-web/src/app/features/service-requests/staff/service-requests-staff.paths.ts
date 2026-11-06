/** The root segment every absolute in-app link starts from. */
const ROOT_SEGMENT = '/';

const STAFF_SEGMENT = 'staff';
const SERVICE_REQUESTS_SEGMENT = 'service-requests';

/** The staff mount inside the staff shell: `/staff/service-requests` (the top route of `SERVICE_REQUESTS_STAFF_ROUTES`). */
export const SERVICE_REQUESTS_STAFF_MOUNT = `${STAFF_SEGMENT}/${SERVICE_REQUESTS_SEGMENT}`;

/**
 * The shell's notifications page (`/notifications`). It mirrors `NOTIFICATIONS_ROUTE_PATH` of features/notifications,
 * which a feature may not import (ESLint boundaries); the staff queue links to it from its toolbar.
 */
const NOTIFICATIONS_SEGMENT = 'notifications';

/** Route parameter names of the staff screens (bound to component inputs of the same name). */
export const ServiceRequestsStaffRouteParams = {
  Id: 'id',
} as const;

/** The child paths of the staff mount. */
export const ServiceRequestsStaffRouteSegments = {
  Queue: '',
  Detail: `:${ServiceRequestsStaffRouteParams.Id}`,
} as const;

function staffLink(...segments: readonly string[]): readonly string[] {
  return [ROOT_SEGMENT, STAFF_SEGMENT, SERVICE_REQUESTS_SEGMENT, ...segments];
}

/** Absolute router commands of the staff screens: `[routerLink]="ServiceRequestsStaffPaths.detail(row.id)"`. */
export const ServiceRequestsStaffPaths = {
  queue: (): readonly string[] => staffLink(),
  detail: (id: string): readonly string[] => staffLink(id),
  notifications: (): readonly string[] => [ROOT_SEGMENT, NOTIFICATIONS_SEGMENT],
} as const;
