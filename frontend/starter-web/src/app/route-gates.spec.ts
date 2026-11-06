// The route-table permission gate (base.fe-permission-gated) over the app's real route table: every route that
// renders a page carries `data.permission` on itself or an ancestor, or its full path is in PUBLIC_ROUTE_PATHS. The shell
// is a layout, exempted by identity (LAYOUT_ROUTES via routesForGate), never by its empty path.
// Lazily loaded child tables are never loaded by the checker; each one is listed in LAZY_ROUTE_TABLES under the full
// path of the route that loads it, and a `loadChildren` route missing from that list fails the gate.
import type { Route, Routes } from '@angular/router';
import { ROUTE_GATE_FIX, RouteFindingKinds, assertRoutesGated, findUngatedRoutes } from '@codelab/ng-tools/routes';
import { describe, expect, it } from 'vitest';
import { PORTAL_ROUTE, SHELL_ROUTE, routes } from './app.routes';
import { ACCESS_ROUTES } from './features/admin/access/access.routes';
import { AdminRouteSegments } from './features/admin/admin.paths';
import { ADMIN_ROUTES } from './features/admin/admin.routes';
import { JOBS_ROUTES } from './features/admin/jobs/jobs.routes';
import { SETTINGS_ROUTES } from './features/admin/settings/settings.routes';
import { PermissionKeys } from './core/permissions/permission-keys.generated';
import { RouteDataKeys, RoutePaths } from './core/routing/route-paths';
import { LAYOUT_ROUTES, PUBLIC_ROUTE_PATHS, SIGNED_IN_ROUTE_PATHS, routesForGate } from './public-routes';

/** Every lazily loaded child table, keyed by the full path of its `loadChildren` route. */
const LAZY_ROUTE_TABLES: Readonly<Record<string, Routes>> = {
  [RoutePaths.Admin]: ADMIN_ROUTES,
  [`${RoutePaths.Admin}/${AdminRouteSegments.Access}`]: ACCESS_ROUTES,
  [`${RoutePaths.Admin}/${AdminRouteSegments.Settings}`]: SETTINGS_ROUTES,
  [`${RoutePaths.Admin}/${AdminRouteSegments.Jobs}`]: JOBS_ROUTES,
};

const RECIPE = 'docs/recipes/add-permission.md';

const GATE_OPTIONS = {
  permissionDataKey: RouteDataKeys.Permission,
  publicPaths: [...PUBLIC_ROUTE_PATHS, ...SIGNED_IN_ROUTE_PATHS],
  lazyChildren: LAZY_ROUTE_TABLES,
  knownPermissions: Object.values(PermissionKeys),
  fix: `${ROUTE_GATE_FIX} Recipe: ${RECIPE}.`,
};

const UNGATED_PATH = 'reports';
const page = () => Promise.resolve(class {});

/** The app's layouts with the staff shell swapped for `shell`: every other layout (the requester shell) stays exempt. */
const layoutsWith = (shell: Route | null): Route[] =>
  LAYOUT_ROUTES.flatMap(layout => (layout === SHELL_ROUTE ? (shell === null ? [] : [shell]) : [layout]));

describe('the route-table permission gate', () => {
  it('lists only staff-shell children as signed-in pages', () => {
    const shellPaths = (SHELL_ROUTE.children ?? []).map(child => child.path);
    for (const path of SIGNED_IN_ROUTE_PATHS) {
      expect(shellPaths).toContain(path);
    }
  });

  it('passes the app routes: every page is gated by a known key or public', () => {
    expect(() => assertRoutesGated(routesForGate(routes), GATE_OPTIONS)).not.toThrow();
  });

  it('lists the sign-in, activation and error paths as public, and never the empty path', () => {
    expect(PUBLIC_ROUTE_PATHS).toEqual(expect.arrayContaining([RoutePaths.Login, RoutePaths.Unauthorized, RoutePaths.SsoCallback, RoutePaths.Activate, '**']));
    expect(PUBLIC_ROUTE_PATHS).not.toContain('');
  });

  it('checks the public portal frame pages at their full paths and fails a portal page without a public path', () => {
    const portal: Route = { ...PORTAL_ROUTE, children: [...(PORTAL_ROUTE.children ?? []), { path: UNGATED_PATH, loadComponent: page }] };
    const withUngated = routesForGate(routes.map(route => (route === PORTAL_ROUTE ? portal : route)), undefined, [portal]);
    expect(findUngatedRoutes(withUngated, GATE_OPTIONS)).toEqual([
      { path: `/${RoutePaths.Portal}/${UNGATED_PATH}`, kind: RouteFindingKinds.missingPermission },
    ]);
  });

  it('fails a page added to the shell without a key, naming its path and the recipe', () => {
    const shell: Route = { ...SHELL_ROUTE, children: [...(SHELL_ROUTE.children ?? []), { path: UNGATED_PATH, loadComponent: page }] };
    const withUngated = routesForGate(routes.map(route => (route === SHELL_ROUTE ? shell : route)), layoutsWith(shell));
    expect(findUngatedRoutes(withUngated, GATE_OPTIONS)).toEqual([{ path: `/${UNGATED_PATH}`, kind: RouteFindingKinds.missingPermission }]);
    expect(() => assertRoutesGated(withUngated, GATE_OPTIONS)).toThrow(RECIPE);
  });

  it('fails a page that carries a key but no permission guard', () => {
    const shell: Route = {
      ...SHELL_ROUTE,
      children: [...(SHELL_ROUTE.children ?? []), { path: UNGATED_PATH, data: { [RouteDataKeys.Permission]: PermissionKeys.DashboardView }, loadComponent: page }],
    };
    const withUnguarded = routesForGate(routes.map(route => (route === SHELL_ROUTE ? shell : route)), layoutsWith(shell));
    expect(findUngatedRoutes(withUnguarded, GATE_OPTIONS)).toEqual([{ path: `/${UNGATED_PATH}`, kind: RouteFindingKinds.missingGuard }]);
  });

  it('fails a second top-level page at the empty path: the shell is exempt by identity, not by path', () => {
    const withEmpty: Routes = [{ path: '', pathMatch: 'full', loadComponent: page }, ...routes];
    expect(findUngatedRoutes(routesForGate(withEmpty), GATE_OPTIONS)).toEqual([{ path: '/', kind: RouteFindingKinds.missingPermission }]);
  });

  it('fails a top-level matcher route (no path) without a key', () => {
    const withMatcher: Routes = [{ matcher: () => null, loadComponent: page }, ...routes];
    expect(findUngatedRoutes(routesForGate(withMatcher), GATE_OPTIONS)).toEqual([{ path: '/', kind: RouteFindingKinds.missingPermission }]);
  });

  it('checks the shell as a page when it is not exempted as a layout', () => {
    expect(findUngatedRoutes(routesForGate(routes, layoutsWith(null)), GATE_OPTIONS)).toEqual([{ path: '/', kind: RouteFindingKinds.missingPermission }]);
  });

  it('refuses a layout route that holds no pages or is not behind authGuard', () => {
    const bare: Route = { path: UNGATED_PATH, loadComponent: page };
    expect(() => routesForGate([bare], [bare])).toThrow(TypeError);
    const unguarded: Route = { path: UNGATED_PATH, loadComponent: page, children: [{ path: UNGATED_PATH, loadComponent: page }] };
    expect(() => routesForGate([unguarded], [unguarded])).toThrow(TypeError);
  });

  it('fails a lazy area whose child table is not listed in LAZY_ROUTE_TABLES', () => {
    const withLazy: Routes = [...routes, { path: UNGATED_PATH, data: { [RouteDataKeys.Permission]: PermissionKeys.DashboardView }, loadChildren: page }];
    expect(findUngatedRoutes(routesForGate(withLazy), GATE_OPTIONS)).toEqual([{ path: `/${UNGATED_PATH}`, kind: RouteFindingKinds.unresolvedLazyChildren }]);
  });
});
