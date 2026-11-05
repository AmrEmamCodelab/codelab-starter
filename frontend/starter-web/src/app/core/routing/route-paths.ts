/** Every top-level route path. Navigation and guards use these; no route literal appears elsewhere. */
export const RoutePaths = {
  Login: 'login',
  Dashboard: '',
  Admin: 'admin',
  Unauthorized: 'unauthorized',
} as const;

export type RoutePath = (typeof RoutePaths)[keyof typeof RoutePaths];

/** Query parameters the core routing reads or writes. */
export const RouteQueryParams = {
  /** The URL to return to after login. */
  ReturnUrl: 'returnUrl',
  /** Why the previous session ended (a {@link SessionEndReason}). */
  Reason: 'reason',
} as const;

/** Route `data` keys the core guards read. */
export const RouteDataKeys = {
  /** The permission key `permissionGuard()` requires, e.g. `{ permission: PermissionKeys.DashboardView }`. */
  Permission: 'permission',
} as const;

/** Why a session ended, shown by the login page as a notice. */
export const SessionEndReasons = {
  Expired: 'expired',
  Idle: 'idle',
} as const;

export type SessionEndReason = (typeof SessionEndReasons)[keyof typeof SessionEndReasons];
