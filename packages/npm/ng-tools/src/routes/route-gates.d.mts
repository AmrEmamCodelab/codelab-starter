// Types for route-gates.mjs (the Angular app imports the checker from a TypeScript spec).

/** The subset of an Angular `Route` the checker reads; nothing on it is called. */
export interface GateRoute {
  readonly path?: string;
  readonly component?: unknown;
  readonly loadComponent?: unknown;
  readonly loadChildren?: unknown;
  readonly redirectTo?: unknown;
  readonly children?: ReadonlyArray<GateRoute>;
  readonly data?: Readonly<Record<string, unknown>>;
  readonly canActivate?: ReadonlyArray<unknown>;
  readonly canActivateChild?: ReadonlyArray<unknown>;
  readonly canMatch?: ReadonlyArray<unknown>;
}

export interface RouteGateOptions {
  /** The route data key holding the permission (the app's `RouteDataKeys.Permission`). */
  readonly permissionDataKey: string;
  /** Full paths (no leading slash) of routes that render without a permission: `login`, `**`, `''`. */
  readonly publicPaths?: Iterable<string>;
  /** Each lazily loaded child array, keyed by the full path of the route whose `loadChildren` loads it. */
  readonly lazyChildren?: ReadonlyMap<string, ReadonlyArray<GateRoute>> | Readonly<Record<string, ReadonlyArray<GateRoute>>>;
  /** When given, a permission outside this set is reported (catches retired or mistyped keys). */
  readonly knownPermissions?: Iterable<string>;
  /** Recognizes the app's permission guard; defaults to {@link isMarkedPermissionGuard}. */
  readonly isPermissionGuard?: (guard: unknown) => boolean;
  /** Replaces the default fix text of the assertion message. */
  readonly fix?: string;
}

export declare const RouteFindingKinds: Readonly<{
  missingPermission: 'missing-permission';
  unknownPermission: 'unknown-permission';
  unresolvedLazyChildren: 'unresolved-lazy-children';
  missingGuard: 'missing-guard';
}>;

/** The symbol markPermissionGuard sets on a guard: `Symbol.for('codelab.ng-tools.permission-guard')`. */
export declare const PERMISSION_GUARD_MARKER: unique symbol;
export declare function markPermissionGuard<T extends (...args: never[]) => unknown>(guard: T): T;
export declare function isMarkedPermissionGuard(guard: unknown): boolean;

export type RouteFindingKind = (typeof RouteFindingKinds)[keyof typeof RouteFindingKinds];

export interface RouteFinding {
  /** The display path, `/admin/users` (`/` for the root). */
  readonly path: string;
  readonly kind: RouteFindingKind;
  readonly permission?: string;
}

export declare const ROUTE_GATE_FIX: string;
export declare function joinRoutePath(parentPath: string, childPath: string | undefined): string;
export declare function displayRoutePath(fullPath: string): string;
export declare function findUngatedRoutes(routes: ReadonlyArray<GateRoute>, options: RouteGateOptions): RouteFinding[];
export declare function formatRouteFindings(findings: ReadonlyArray<RouteFinding>, fix?: string): string;
export declare function assertRoutesGated(routes: ReadonlyArray<GateRoute>, options: RouteGateOptions): void;
