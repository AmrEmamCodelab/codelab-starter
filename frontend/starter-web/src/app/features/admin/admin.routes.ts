import type { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.guard';
import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../core/routing/route-paths';
import { AdminRouteSegments } from './admin.paths';

function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The admin area, mounted under `/admin` by `app.routes.ts` (`loadChildren`, open at the mount to a holder of any
 * key in `ADMIN_AREA_PERMISSIONS`). Each page is gated again by its own key with `permissionGuard()`; the access screens (`ACCESS_ROUTES`,
 * owned by `features/admin/access`) gate each of their pages themselves.
 */
export const ADMIN_ROUTES: Routes = [
  {
    path: AdminRouteSegments.Home,
    pathMatch: 'full',
    canActivate: [permissionGuard()],
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
  {
    path: AdminRouteSegments.Access,
    loadChildren: () => import('./access/access.routes').then((m) => m.ACCESS_ROUTES),
  },
];
