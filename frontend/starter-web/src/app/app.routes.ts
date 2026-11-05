import type { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { permissionGuard } from './core/auth/permission.guard';
import { PermissionKeys, type PermissionKey } from './core/permissions/permission-keys.generated';
import { RouteDataKeys, RoutePaths } from './core/routing/route-paths';

/** The empty path the staff shell hangs on: its children carry the real paths from {@link RoutePaths}. */
export const SHELL_PATH = '';

/** Matches every URL no other route claims: the 404 page. */
export const NOT_FOUND_PATH = '**';

/** Route data naming the permission `permissionGuard()` requires. */
function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * Every route of the app. Pages are lazy; each page inside the shell is gated by a typed permission key
 * (`permissionGuard()` reads it from route data and fails closed). The login, 403 and 404 pages stand
 * outside the shell so they render without a session.
 */
export const routes: Routes = [
  {
    path: RoutePaths.Login,
    loadComponent: () => import('./features/auth/login-page/login-page.component').then(m => m.LoginPageComponent),
  },
  {
    path: RoutePaths.Unauthorized,
    loadComponent: () =>
      import('./features/errors/unauthorized/unauthorized-page.component').then(m => m.UnauthorizedPageComponent),
  },
  {
    path: SHELL_PATH,
    canActivate: [authGuard],
    loadComponent: () => import('./layouts/app-shell/app-shell.component').then(m => m.AppShellComponent),
    children: [
      {
        path: RoutePaths.Dashboard,
        pathMatch: 'full',
        canActivate: [permissionGuard()],
        data: requires(PermissionKeys.DashboardView),
        loadComponent: () =>
          import('./features/dashboard/dashboard-page/dashboard-page.component').then(m => m.DashboardPageComponent),
      },
      {
        path: RoutePaths.Admin,
        canActivate: [permissionGuard()],
        data: requires(PermissionKeys.AccessAdminView),
        loadComponent: () =>
          import('./features/admin/admin-page/admin-page.component').then(m => m.AdminPageComponent),
      },
    ],
  },
  {
    path: NOT_FOUND_PATH,
    loadComponent: () =>
      import('./features/errors/not-found/not-found-page.component').then(m => m.NotFoundPageComponent),
  },
];
