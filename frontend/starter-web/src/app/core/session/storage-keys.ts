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
  LastActivity: 'starter.session.last-activity',
  Language: 'starter.language',
} as const;

export type StorageKey = (typeof StorageKeys)[keyof typeof StorageKeys];
