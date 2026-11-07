/** The console's route paths, relative to the base href (`overview` is `/ops/overview` under a `/ops/` base). */
export const RoutePaths = {
  Overview: 'overview',
  Reference: 'reference',
  Jobs: 'jobs',
  Certificates: 'certificates',
  SignInRequired: 'sign-in-required',
  AccessDenied: 'access-denied',
} as const;

export type RoutePath = (typeof RoutePaths)[keyof typeof RoutePaths];

/** Query parameter names. `code` makes a reference-code lookup a shareable link. */
export const QueryParams = {
  Code: 'code',
} as const;

/** Route data keys. */
export const RouteDataKeys = {
  Refusal: 'refusal',
} as const;

/** Why the console refuses the operator: no Windows identity reached the BFF (401) or the identity lacks the group (403). */
export enum AccessRefusal {
  Unauthenticated = 1,
  Forbidden = 2,
}
