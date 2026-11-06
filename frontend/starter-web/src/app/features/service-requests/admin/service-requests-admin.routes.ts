import type { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import type { PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { SERVICE_REQUESTS_ADMIN_MOUNT, ServiceRequestsAdminRouteSegments } from './service-requests-admin.paths';
import { ServiceRequestsAdminPermissionKeys } from './service-requests-admin.permissions';

function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The admin oversight of the Service Request sample, spread into `ADMIN_ROUTES`
 * (`...SERVICE_REQUESTS_ADMIN_ROUTES, // codelab:module ServiceRequests`): the read-only "all requests" list at
 * `/admin/requests` and the read-only detail at `/admin/requests/:id`, both gated by `records.oversight.view`.
 */
export const SERVICE_REQUESTS_ADMIN_ROUTES: Routes = [
  {
    path: SERVICE_REQUESTS_ADMIN_MOUNT,
    children: [
      {
        path: ServiceRequestsAdminRouteSegments.List,
        pathMatch: 'full',
        canActivate: [permissionGuard()],
        data: requires(ServiceRequestsAdminPermissionKeys.Oversight),
        loadComponent: () =>
          import('./service-request-oversight-page.component').then((m) => m.ServiceRequestOversightPageComponent),
      },
      {
        path: ServiceRequestsAdminRouteSegments.Detail,
        canActivate: [permissionGuard()],
        data: requires(ServiceRequestsAdminPermissionKeys.Oversight),
        loadComponent: () =>
          import('./service-request-admin-detail-page.component').then((m) => m.ServiceRequestAdminDetailPageComponent),
      },
    ],
  },
];
