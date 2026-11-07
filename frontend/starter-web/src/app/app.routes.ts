import type { Route, Routes } from '@angular/router';
import { APP_NAV_ITEMS } from './app-nav';
import { authGuard } from './core/auth/auth.guard';
import { permissionGuard } from './core/auth/permission.guard';
import { PermissionKeys, type PermissionKey } from './core/permissions/permission-keys.generated';
import { RouteDataKeys, RoutePaths } from './core/routing/route-paths';
import { ADMIN_AREA_PERMISSIONS } from './features/admin/admin-entry';
import { ATTACHMENTS_ROUTES } from './features/attachments/attachments.routes'; // codelab:module Attachments
import { NOTIFICATIONS_LINK, NOTIFICATIONS_ROUTES } from './features/notifications/notifications.routes';
// codelab:module-begin ServiceRequests
import { SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK, SERVICE_REQUESTS_REQUESTER_NAV, SERVICE_REQUESTS_REQUESTER_ROUTES, requesterLandingGuard, requesterShellMatch } from './features/service-requests/requester';
import { SERVICE_REQUESTS_STAFF_ROUTES } from './features/service-requests/staff/service-requests-staff.routes';
import { REQUESTER_SHELL_OPTIONS } from './layouts/requester-shell/requester-nav';
// codelab:module-end ServiceRequests
import { PORTAL_ROUTES } from './features/portal/portal.routes'; // codelab:module Portal
import { SHELL_NAV_ITEMS } from './layouts/app-shell/shell-nav';
import { SHOWCASE_ROUTES } from './showcase/showcase.routes';

/** The empty path the staff shell hangs on: its children carry the real paths from {@link RoutePaths}. */
export const SHELL_PATH = '';

/** Matches every URL no other route claims: the 404 page. */
export const NOT_FOUND_PATH = '**';

/** Route data naming the permission `permissionGuard()` requires. */
function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The staff shell: a layout behind `authGuard` that renders no page content of its own. Each page inside it carries
 * its own permission key. The route gate exempts this route by identity (`LAYOUT_ROUTES` in `public-routes.ts`),
 * never by its empty path.
 */
export const SHELL_ROUTE: Route = {
  path: SHELL_PATH,
  canActivate: [authGuard],
  providers: [{ provide: SHELL_NAV_ITEMS, useValue: APP_NAV_ITEMS }],
  loadComponent: () => import('./layouts/app-shell/app-shell.component').then(m => m.AppShellComponent),
  children: [
    {
      path: RoutePaths.Dashboard,
      pathMatch: 'full',
      // A requester without dashboard.view lands on the requester dashboard; staff and admins stay here.
      canActivate: [
        requesterLandingGuard, // codelab:module ServiceRequests
        permissionGuard(),
      ],
      data: requires(PermissionKeys.DashboardView),
      loadComponent: () =>
        import('./features/dashboard/dashboard-page/dashboard-page.component').then(m => m.DashboardPageComponent),
    },
    {
      // A holder of any admin page key may enter; each page under the mount is gated again by its own key.
      path: RoutePaths.Admin,
      canActivate: [permissionGuard(ADMIN_AREA_PERMISSIONS)],
      loadChildren: () => import('./features/admin/admin.routes').then(m => m.ADMIN_ROUTES),
    },
    ...NOTIFICATIONS_ROUTES,
    ...SERVICE_REQUESTS_STAFF_ROUTES, // codelab:module ServiceRequests
    // Development builds only (canMatch); gated by attachments.view. // codelab:module Attachments
    ...ATTACHMENTS_ROUTES, // codelab:module Attachments
  ],
};

// codelab:module-begin ServiceRequests
/**
 * The requester shell (behind authGuard); each page inside carries its own key. `canMatch` limits it to the requester
 * area, so `/` falls through to the staff shell instead of an empty requester frame.
 */
export const REQUESTER_SHELL_ROUTE: Route = {
  path: SHELL_PATH,
  canMatch: [requesterShellMatch],
  canActivate: [authGuard],
  providers: [
    {
      provide: REQUESTER_SHELL_OPTIONS,
      useValue: {
        navItems: SERVICE_REQUESTS_REQUESTER_NAV,
        homeLink: SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK,
        notificationsLink: NOTIFICATIONS_LINK,
      },
    },
  ],
  loadComponent: () =>
    import('./layouts/requester-shell/requester-shell.component').then(m => m.RequesterShellComponent),
  children: [...SERVICE_REQUESTS_REQUESTER_ROUTES],
};
// codelab:module-end ServiceRequests

/**
 * The public portal frame (no session): the portal shell around the public pages, each one public. `children` is
 * eager on purpose: only the small routes file is imported; every page is lazy. The route gate checks its children as
 * top-level pages (`PUBLIC_LAYOUT_ROUTES` in `public-routes.ts`).
 */
export const PORTAL_ROUTE: Route = {
  path: RoutePaths.Portal,
  loadComponent: () => import('./layouts/portal-shell/portal-shell.component').then(m => m.PortalShellComponent),
  children: PORTAL_ROUTES, // codelab:module Portal
};

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
    path: RoutePaths.SsoCallback,
    loadComponent: () =>
      import('./features/auth/sso-callback/sso-callback-page.component').then(m => m.SsoCallbackPageComponent),
  },
  {
    path: RoutePaths.Activate,
    loadComponent: () => import('./features/auth/activate/activate-page.component').then(m => m.ActivatePageComponent),
  },
  {
    path: RoutePaths.Unauthorized,
    loadComponent: () =>
      import('./features/errors/unauthorized/unauthorized-page.component').then(m => m.UnauthorizedPageComponent),
  },
  PORTAL_ROUTE,
  ...SHOWCASE_ROUTES,
  REQUESTER_SHELL_ROUTE, // codelab:module ServiceRequests
  SHELL_ROUTE,
  {
    path: NOT_FOUND_PATH,
    loadComponent: () =>
      import('./features/errors/not-found/not-found-page.component').then(m => m.NotFoundPageComponent),
  },
];
