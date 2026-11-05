import { InjectionToken } from '@angular/core';

/** Runs `task` while holding the named lock, shared by every tab of this origin. */
export type CrossTabLock = <T>(name: string, task: () => Promise<T>) => Promise<T>;

/** Every cross-tab lock name the app takes. One table, so a fork renames them in one place. */
export const CrossTabLockNames = {
  /** Held around a token refresh, so two tabs never send the same refresh token. */
  SessionRefresh: 'starter.session.refresh',
} as const;

/** Runs the task with no lock: the fallback for a browser without the Web Locks API (and for specs). */
export const noCrossTabLock: CrossTabLock = (_name, task) => task();

interface LockManagerLike {
  request<T>(name: string, task: () => Promise<T>): Promise<T>;
}

/** The Web Locks API of this browser, or null when it has none. */
function browserLocks(): LockManagerLike | null {
  const locks = (globalThis.navigator as { locks?: LockManagerLike } | undefined)?.locks;
  return locks && typeof locks.request === 'function' ? locks : null;
}

/**
 * The cross-tab lock: `navigator.locks` when the browser has it, else {@link noCrossTabLock}. Without the
 * lock, `AuthService` still re-reads the stored tokens before it refreshes, which closes all but a
 * same-instant race between tabs.
 */
export const CROSS_TAB_LOCK = new InjectionToken<CrossTabLock>('CROSS_TAB_LOCK', {
  providedIn: 'root',
  factory: () => {
    const locks = browserLocks();
    return locks ? (name, task) => locks.request(name, task) : noCrossTabLock;
  },
});
