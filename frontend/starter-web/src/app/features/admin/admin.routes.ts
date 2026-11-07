import type { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.guard';
import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../core/routing/route-paths';
import { ADMIN_AREA_PERMISSIONS } from './admin-entry';
import { AdminRouteSegments } from './admin.paths';
import { LOOKUPS_ROUTES } from './lookups/lookups.routes';
import { PORTAL_CONTENT_ROUTES } from './portal-content/portal-content.routes'; // codelab:module Portal
import { SERVICE_REQUESTS_ADMIN_ROUTES } from '../service-requests/admin/service-requests-admin.routes'; // codelab:module ServiceRequests

function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The admin area, mounted under `/admin` by `app.routes.ts` (`loadChildren`, open at the mount to a holder of any
 * key in `ADMIN_AREA_PERMISSIONS`). Each page is gated again by its own key with `permissionGuard()`; the access screens (`ACCESS_ROUTES`,
 * owned by `features/admin/access`) and the runtime settings and background jobs screens (`SETTINGS_ROUTES`, `JOBS_ROUTES`) gate
 * each of their pages themselves; the lookups and portal content routes carry their own keys and guards.
 */
export const ADMIN_ROUTES: Routes = [
  {
    path: AdminRouteSegments.Home,
    pathMatch: 'full',
    // Open to a holder of any admin key: the home lists only the tiles whose keys the user holds, so a scoped admin
    // (say lookups.view + settings.view) can reach each of their pages from it. The data key names the home's own key.
    canActivate: [permissionGuard(ADMIN_AREA_PERMISSIONS)],
    data: requires(PermissionKeys.AccessAdminView),
    loadComponent: () => import('./admin-page/admin-page.component').then((m) => m.AdminPageComponent),
  },
  {
    path: AdminRouteSegments.Users,
    canActivate: [permissionGuard()],
    data: requires(PermissionKeys.UsersView),
    loadComponent: () => import('./users/users-page.component').then((m) => m.UsersPageComponent),
  },
  {
    path: AdminRouteSegments.Organizations,
    canActivate: [permissionGuard()],
    data: requires(PermissionKeys.OrganizationsView),
    loadComponent: () =>
      import('./organizations/organizations-page.component').then((m) => m.OrganizationsPageComponent),
  },
  {
    path: AdminRouteSegments.Import,
    canActivate: [permissionGuard()],
    data: requires(PermissionKeys.UsersImport),
    loadComponent: () => import('./import/user-import-page.component').then((m) => m.UserImportPageComponent),
  },
  ...LOOKUPS_ROUTES,
  ...PORTAL_CONTENT_ROUTES, // codelab:module Portal
  ...SERVICE_REQUESTS_ADMIN_ROUTES, // codelab:module ServiceRequests
  {
    path: AdminRouteSegments.Access,
    loadChildren: () => import('./access/access.routes').then((m) => m.ACCESS_ROUTES),
  },
  {
    path: AdminRouteSegments.Settings,
    loadChildren: () => import('./settings/settings.routes').then((m) => m.SETTINGS_ROUTES),
  },
  {
    path: AdminRouteSegments.Jobs,
    loadChildren: () => import('./jobs/jobs.routes').then((m) => m.JOBS_ROUTES),
  },
];
