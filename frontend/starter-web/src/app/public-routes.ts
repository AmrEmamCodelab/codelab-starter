import type { Route, Routes } from '@angular/router';
import { NOT_FOUND_PATH, PORTAL_ROUTE, SHELL_ROUTE } from './app.routes';
import { authGuard } from './core/auth/auth.guard';
import { RoutePaths } from './core/routing/route-paths';
import { PORTAL_PUBLIC_PATHS } from './features/portal/portal.routes';
import { SHOWCASE_PUBLIC_PATHS } from './showcase/showcase.routes';

/**
 * The full paths (no leading slash) of the routes that render without a permission key: the route-table gate
 * (`route-gates.spec.ts`, base.fe-permission-gated) fails every other page route that has no `data.permission` on
 * itself or an ancestor. An entry exempts only the outermost route with that full path.
 *
 * Add a path here only for a page every visitor may open (sign-in, activation, error pages). A page for signed-in
 * users is gated by a permission key instead (docs/recipes/add-permission.md). The empty path is never listed: it
 * would also exempt any other top-level page at '' and every `matcher` route (which has no path). The shell is
 * exempted by identity through {@link LAYOUT_ROUTES} instead.
 */
export const PUBLIC_ROUTE_PATHS: readonly string[] = [
  RoutePaths.Login,
  RoutePaths.Unauthorized,
  RoutePaths.SsoCallback,
  RoutePaths.Activate,
  NOT_FOUND_PATH,
  ...PORTAL_PUBLIC_PATHS,
  ...SHOWCASE_PUBLIC_PATHS,
];

/**
 * The public layout routes, matched by object identity at the top level: each renders a public frame (no session)
 * around public pages. The gate replaces the frame with its children, hoisted to the top level at their full paths, so
 * an empty child (the frame's landing page) is exempted by its own full path and a child without a public path fails.
 */
export const PUBLIC_LAYOUT_ROUTES: readonly Route[] = [PORTAL_ROUTE];

/**
 * The layout routes, matched by object identity: each renders a frame behind `authGuard` and holds its pages as
 * `children`, every one of which carries its own key. A layout renders no page content, so the gate checks only its
 * children.
 */
export const LAYOUT_ROUTES: readonly Route[] = [SHELL_ROUTE];

/**
 * The route table as the gate checks it: every {@link LAYOUT_ROUTES} entry (found by identity, at any depth) loses
 * its component, so only its children are checked. Every other route, whatever its path, is checked as a page.
 *
 * @throws TypeError when a layout route has no children or is not behind `authGuard`: it would then be a page that
 *   renders without a key.
 */
export function routesForGate(
  table: Routes,
  layouts: readonly Route[] = LAYOUT_ROUTES,
  publicLayouts: readonly Route[] = PUBLIC_LAYOUT_ROUTES,
): Routes {
  return hoistPublicLayouts(table, publicLayouts).map(route => {
    const children = route.children === undefined ? undefined : routesForGate(route.children, layouts);
    if (!layouts.includes(route)) {
      return children === undefined ? route : { ...route, children };
    }
    assertLayout(route);
    return { ...route, component: undefined, loadComponent: undefined, children };
  });
}

function hoistPublicLayouts(table: Routes, publicLayouts: readonly Route[]): Routes {
  return table.flatMap(route =>
    publicLayouts.includes(route)
      ? (route.children ?? []).map(child => ({ ...child, path: [route.path, child.path].filter(Boolean).join('/') }))
      : [route],
  );
}

function assertLayout(route: Route): void {
  const hasPages = (route.children?.length ?? 0) > 0;
  if (!hasPages || !(route.canActivate ?? []).includes(authGuard)) {
    throw new TypeError(`Layout route '${route.path ?? 'matcher'}' must hold its pages as children behind authGuard.`);
  }
}
