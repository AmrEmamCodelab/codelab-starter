// The route-table permission gate (base.fe-permission-gated): every route that renders a page (`component` or
// `loadComponent`) carries a permission key in its route data, on itself or on an ancestor, unless its full path is
// in the public allowlist; and the key that gates the page is enforced, because route data alone enforces nothing.
// A permission guard reads the data of the route it runs for, so the effective (innermost) key is enforced only when
// the route that declares it runs a permission guard in canActivate or canMatch, or a strict ancestor of that route
// runs one in canActivateChild (Angular never runs a route's own canActivateChild for the route itself, and an outer
// guard checks the outer key, not a descendant's). A guard counts when it carries the
// PERMISSION_GUARD_MARKER symbol (set by markPermissionGuard) or when options.isPermissionGuard accepts it. Redirects
// render nothing and are exempt. Meant for a Vitest spec in the app:
//
//   import { assertRoutesGated } from '@codelab/ng-tools/routes';
//   assertRoutesGated(routes, {
//     permissionDataKey: RouteDataKeys.Permission,
//     publicPaths: PUBLIC_ROUTE_PATHS,
//     lazyChildren: { admin: ADMIN_ROUTES, 'admin/access': ACCESS_ROUTES },
//   });
//
// The checker never calls `loadChildren` (a lazy import would load the whole feature): the caller passes each lazily
// loaded child array in `lazyChildren`, keyed by the full path of the route that loads it. A `loadChildren` route
// without an entry is reported, so a new lazy area cannot slip past the check.

/** Why a route failed the check. */
export const RouteFindingKinds = Object.freeze({
  /** The route renders a page and neither it nor an ancestor carries a permission key. */
  missingPermission: 'missing-permission',
  /** The route carries a permission key that is not in `knownPermissions`. */
  unknownPermission: 'unknown-permission',
  /** The route lazy-loads children and `lazyChildren` has no entry for its full path. */
  unresolvedLazyChildren: 'unresolved-lazy-children',
  /** The route's effective permission key is not enforced: the route declaring it runs no permission guard in
   *  canActivate or canMatch, and no ancestor of that route runs one in canActivateChild. */
  missingGuard: 'missing-guard',
});

/** Process-wide (Symbol.for) so an app can tag its guard without importing this tooling into its bundle. */
export const PERMISSION_GUARD_MARKER = Symbol.for('codelab.ng-tools.permission-guard');

/** Guard properties that run for the route that declares them (and so check that route's own data). */
const OWN_GUARD_PROPERTIES = Object.freeze(['canActivate', 'canMatch']);
/** Guard properties that run for every descendant of the route that declares them, never for the route itself. */
const CHILD_GUARD_PROPERTIES = Object.freeze(['canActivateChild']);

/** Tags a guard function (what permissionGuard() returns) so the checker recognizes it; returns the same function. */
export function markPermissionGuard(guard) {
  if (typeof guard !== 'function') throw new TypeError('markPermissionGuard: the guard must be a function.');
  Object.defineProperty(guard, PERMISSION_GUARD_MARKER, { value: true });
  return guard;
}

/** True for a guard tagged by {@link markPermissionGuard}. */
export function isMarkedPermissionGuard(guard) {
  return typeof guard === 'function' && guard[PERMISSION_GUARD_MARKER] === true;
}

export const ROUTE_GATE_FIX =
  'Give the route data: { [RouteDataKeys.Permission]: PermissionKeys.<Key> } and canActivate: [permissionGuard()] on the same route '
  + '(the guard checks the data of the route it is attached to), '
  + 'or, for a page that must render without a permission, add its full path to the public route allowlist.';

const SEGMENT_SEPARATOR = '/';
const EDGE_SLASHES = /^\/+|\/+$/g;
const ROOT_DISPLAY = '/';

/** `a/b` from a parent full path and a child path; empty segments vanish, as in the Angular router. */
export function joinRoutePath(parentPath, childPath) {
  return [parentPath, childPath ?? '']
    .map((part) => part.replace(EDGE_SLASHES, ''))
    .filter((part) => part !== '')
    .join(SEGMENT_SEPARATOR);
}

/** The full path as a URL for messages: `/admin/users`, `/` for the root. */
export function displayRoutePath(fullPath) {
  return fullPath === '' ? ROOT_DISPLAY : `${ROOT_DISPLAY}${fullPath}`;
}

function normalizeOptions(options) {
  if (!options || typeof options.permissionDataKey !== 'string' || options.permissionDataKey === '') {
    throw new TypeError('assertRoutesGated: options.permissionDataKey (the route data key holding the permission) is required.');
  }
  if (options.isPermissionGuard !== undefined && typeof options.isPermissionGuard !== 'function') {
    throw new TypeError('assertRoutesGated: options.isPermissionGuard must be a function.');
  }
  const lazy = options.lazyChildren instanceof Map ? options.lazyChildren : new Map(Object.entries(options.lazyChildren ?? {}));
  return {
    permissionDataKey: options.permissionDataKey,
    publicPaths: new Set([...(options.publicPaths ?? [])].map((path) => path.replace(EDGE_SLASHES, ''))),
    lazyChildren: new Map([...lazy].map(([path, children]) => [path.replace(EDGE_SLASHES, ''), children])),
    knownPermissions: options.knownPermissions ? new Set(options.knownPermissions) : null,
    isPermissionGuard: options.isPermissionGuard ?? isMarkedPermissionGuard,
  };
}

function permissionOf(route, key) {
  const value = route.data?.[key];
  return typeof value === 'string' && value.trim() !== '' ? value : null;
}

const rendersPage = (route) => route.component !== undefined || route.loadComponent !== undefined;

/** True when one of the route's `properties` guard lists holds a permission guard. */
function runsPermissionGuard(route, properties, isPermissionGuard) {
  return properties.some((property) => Array.isArray(route[property]) && route[property].some((guard) => isPermissionGuard(guard)));
}

/**
 * Every problem in a route table, depth first in declaration order.
 * @param {ReadonlyArray<object>} routes an Angular `Routes` array (plain objects; nothing is called)
 * @param {{ permissionDataKey: string, publicPaths?: Iterable<string>,
 *   lazyChildren?: Map<string, ReadonlyArray<object>> | Record<string, ReadonlyArray<object>>,
 *   knownPermissions?: Iterable<string>, isPermissionGuard?: (guard: unknown) => boolean }} options
 * @returns {{ path: string, kind: string, permission?: string }[]} `path` is the display path (`/admin/users`)
 */
export function findUngatedRoutes(routes, options) {
  const settings = normalizeOptions(options);
  const findings = [];
  const visit = (list, parent) => {
    for (const route of list ?? []) visitRoute(route, parent, settings, findings, visit);
  };
  // gated: an effective key exists; keyEnforced: that key's declaring route is guarded; childGuarded: an ancestor
  // runs a permission guard in canActivateChild (it then runs for every route below it).
  visit(routes, { fullPath: null, gated: false, keyEnforced: false, childGuarded: false });
  return findings;
}

function visitRoute(route, parent, settings, findings, visit) {
  if (route.redirectTo !== undefined) return;
  const fullPath = joinRoutePath(parent.fullPath ?? '', route.path);
  const display = displayRoutePath(fullPath);
  const permission = permissionOf(route, settings.permissionDataKey);
  if (permission !== null && settings.knownPermissions && !settings.knownPermissions.has(permission)) {
    findings.push({ path: display, kind: RouteFindingKinds.unknownPermission, permission });
  }
  const gated = parent.gated || permission !== null;
  // The innermost key wins; a key declared here is enforced only by this route's own canActivate/canMatch or by an
  // ancestor's canActivateChild, never by an ancestor's canActivate (that checks the ancestor's own key).
  const keyEnforced = permission === null
    ? parent.keyEnforced
    : parent.childGuarded || runsPermissionGuard(route, OWN_GUARD_PROPERTIES, settings.isPermissionGuard);
  const childGuarded = parent.childGuarded || runsPermissionGuard(route, CHILD_GUARD_PROPERTIES, settings.isPermissionGuard);
  // A public path exempts the outermost route with that full path only: a pathless child of a public route (the
  // shell's dashboard under the empty shell path) still needs its own key.
  const isPublic = settings.publicPaths.has(fullPath) && fullPath !== parent.fullPath;
  if (rendersPage(route) && !gated && !isPublic) {
    findings.push({ path: display, kind: RouteFindingKinds.missingPermission });
  } else if (rendersPage(route) && gated && !keyEnforced && !isPublic) {
    findings.push({ path: display, kind: RouteFindingKinds.missingGuard });
  }
  const next = { fullPath, gated, keyEnforced, childGuarded };
  visit(route.children, next);
  if (route.loadChildren === undefined) return;
  if (!settings.lazyChildren.has(fullPath)) {
    findings.push({ path: display, kind: RouteFindingKinds.unresolvedLazyChildren });
    return;
  }
  visit(settings.lazyChildren.get(fullPath), next);
}

const FINDING_TEXT = Object.freeze({
  [RouteFindingKinds.missingPermission]: () => 'renders a page without a permission key',
  [RouteFindingKinds.unknownPermission]: (finding) => `requires '${finding.permission}', which is not a known permission key`,
  [RouteFindingKinds.unresolvedLazyChildren]: () => 'lazy-loads children that were not passed in lazyChildren (key: its full path)',
  [RouteFindingKinds.missingGuard]: () => 'has a permission key but no permission guard enforces it (canActivate or canMatch on the route declaring the key, or canActivateChild on an ancestor of it)',
});

/** One line per finding plus the fix, for an assertion message or a CLI. */
export function formatRouteFindings(findings, fix = ROUTE_GATE_FIX) {
  const lines = findings.map((finding) => `  ${finding.path} ${FINDING_TEXT[finding.kind](finding)}`);
  return `Route permission gate (base.fe-permission-gated) failed for ${findings.length} route(s):\n${lines.join('\n')}\nFix: ${fix}`;
}

/** Throws an Error listing every ungated route (see {@link findUngatedRoutes}); returns nothing when all pass. */
export function assertRoutesGated(routes, options) {
  const findings = findUngatedRoutes(routes, options);
  if (findings.length > 0) throw new Error(formatRouteFindings(findings, options.fix));
}
