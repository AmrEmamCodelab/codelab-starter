/** Every top-level route path. Navigation and guards use these; no route literal appears elsewhere. */
export const RoutePaths = {
  Login: 'login',
  Dashboard: '',
  Admin: 'admin',
  Unauthorized: 'unauthorized',
  /** The SSO callback page (public): reads the one-time exchange ticket from the URL fragment. */
  SsoCallback: 'auth/sso/callback',
  /** The account activation page (public): sets the first password from an activation ticket. */
  Activate: 'activate',
} as const;

export type RoutePath = (typeof RoutePaths)[keyof typeof RoutePaths];

/** Query parameters the core routing reads or writes. */
export const RouteQueryParams = {
  /** The URL to return to after login. */
  ReturnUrl: 'returnUrl',
  /** Why the previous session ended (a {@link SessionEndReason}). */
  Reason: 'reason',
  /** A success notice the login page shows (a {@link LoginNotice}), e.g. after an account activation. */
  Notice: 'notice',
} as const;

/** The success notices the login page shows for `?notice=`. */
export const LoginNotices = {
  /** The account's first password was set from an activation link. */
  Activated: 'activated',
} as const;

export type LoginNotice = (typeof LoginNotices)[keyof typeof LoginNotices];

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
