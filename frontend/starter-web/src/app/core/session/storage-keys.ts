/**
 * Every browser-storage key the app writes. One table, so a fork renames them in one place and no two
 * services collide on a key.
 */
export const StorageKeys = {
  AccessToken: 'starter.session.access-token',
  RefreshToken: 'starter.session.refresh-token',
  AccessTokenExpiresAt: 'starter.session.access-token-expires-at',
  IdleTimeoutMinutes: 'starter.session.idle-timeout-minutes',
  SessionUser: 'starter.session.user',
  /** The `SignInMode` of the stored session (a number). */
  SignInMode: 'starter.session.sign-in-mode',
  /** The app-relative URL an SSO sign-in returns to, kept across the identity provider round trip. */
  SsoReturnUrl: 'starter.sso.return-url',
  /** The SSO browser-binding secret of the sign-in this tab started (sessionStorage, read once by the callback). */
  SsoBindingSecret: 'starter.sso.binding-secret',
  LastActivity: 'starter.session.last-activity',
  Language: 'starter.language',
} as const;

export type StorageKey = (typeof StorageKeys)[keyof typeof StorageKeys];
