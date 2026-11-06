import type { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import type { PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { SERVICE_REQUESTS_STAFF_MOUNT, ServiceRequestsStaffRouteSegments } from './service-requests-staff.paths';
import { ServiceRequestsStaffPermissionKeys } from './service-requests-staff.permissions';

function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The staff screens of the Service Request sample, spread into the staff shell's children by `app.routes.ts`
 * (`...SERVICE_REQUESTS_STAFF_ROUTES, // codelab:module ServiceRequests`): the work queue at `/staff/service-requests`
 * and the staff detail at `/staff/service-requests/:id`, both gated by the record type's queue permission
 * (`service-requests.queue`). Claim / release and the decisions are gated inside the pages (`assignments.work`, the
 * server's allowed actions).
 */
export const SERVICE_REQUESTS_STAFF_ROUTES: Routes = [
  {
    path: SERVICE_REQUESTS_STAFF_MOUNT,
    children: [
      {
        path: ServiceRequestsStaffRouteSegments.Queue,
        pathMatch: 'full',
        canActivate: [permissionGuard()],
        data: requires(ServiceRequestsStaffPermissionKeys.Queue),
        loadComponent: () =>
          import('./service-request-queue-page.component').then((m) => m.ServiceRequestQueuePageComponent),
      },
      {
        path: ServiceRequestsStaffRouteSegments.Detail,
        canActivate: [permissionGuard()],
        data: requires(ServiceRequestsStaffPermissionKeys.Queue),
        loadComponent: () =>
          import('./service-request-staff-detail-page.component').then((m) => m.ServiceRequestStaffDetailPageComponent),
      },
    ],
  },
];
