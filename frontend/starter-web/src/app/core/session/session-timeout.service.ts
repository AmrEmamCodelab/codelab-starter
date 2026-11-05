import { DOCUMENT, DestroyRef, Injectable, Injector, effect, inject, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { TokenStore } from '../auth/token.store';
import { SessionEndReasons } from '../routing/route-paths';
import { AppStorage } from './app-storage';
import { APP_CLOCK } from './clock';
import { StorageKeys } from './storage-keys';
import { MILLISECONDS_PER_MINUTE, MILLISECONDS_PER_SECOND } from './time-units';

/** User input that counts as activity. */
export const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart'] as const;

/** Seconds between shared activity writes (see ACTIVITY_WRITE_THROTTLE_MS). */
const ACTIVITY_WRITE_THROTTLE_SECONDS = 5;

/**
 * At most one shared activity write per tab per this many ms: pointer moves fire continuously and every
 * write raises a `storage` event in every other tab. The shared time can lag by this much, which expiry
 * allows for.
 */
export const ACTIVITY_WRITE_THROTTLE_MS = ACTIVITY_WRITE_THROTTLE_SECONDS * MILLISECONDS_PER_SECOND;

/** The `storage` DOM event name. */
const STORAGE_EVENT = 'storage';

/**
 * Ends an idle session. The window is the login response's `idleTimeoutMinutes` (0 or less disables it).
 * Idle means idle in EVERY tab: the session is shared by the user's tabs, so each tab publishes its
 * activity to browser storage, re-arms when another tab publishes, and re-checks the shared time
 * before it expires the session.
 */
@Injectable({ providedIn: 'root' })
export class SessionTimeoutService {
  private readonly auth = inject(AuthService);
  private readonly tokens = inject(TokenStore);
  private readonly router = inject(Router);
  private readonly storage = inject(AppStorage);
  private readonly clock = inject(APP_CLOCK);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly injector = inject(Injector);

  private timer: ReturnType<typeof setTimeout> | undefined;
  private timeoutMs = 0;
  private watching = false;
  private started = false;
  private lastWrite = 0;

  private readonly onActivity = (): void => {
    this.arm(this.timeoutMs);
    this.publishActivity(false);
  };

  private readonly onStorage = (event: StorageEvent): void => {
    if (event.key === StorageKeys.LastActivity && event.newValue) {
      this.arm(this.timeoutMs);
    }
  };

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopWatching());
  }

  /** Follows the session: watches while signed in, stops when signed out. Idempotent. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    effect(
      () => {
        const minutes = this.tokens.session()?.idleTimeoutMinutes ?? 0;
        untracked(() => (minutes > 0 ? this.startWatching(minutes * MILLISECONDS_PER_MINUTE) : this.stopWatching()));
      },
      { injector: this.injector },
    );
  }

  private startWatching(timeoutMs: number): void {
    this.timeoutMs = timeoutMs;
    if (!this.watching) {
      this.watching = true;
      ACTIVITY_EVENTS.forEach(name => this.window?.addEventListener(name, this.onActivity, { passive: true }));
      this.window?.addEventListener(STORAGE_EVENT, this.onStorage);
    }
    this.arm(timeoutMs);
    this.publishActivity(true);
  }

  private stopWatching(): void {
    this.clearTimer();
    if (!this.watching) {
      return;
    }
    this.watching = false;
    ACTIVITY_EVENTS.forEach(name => this.window?.removeEventListener(name, this.onActivity));
    this.window?.removeEventListener(STORAGE_EVENT, this.onStorage);
  }

  private arm(delayMs: number): void {
    this.clearTimer();
    this.timer = setTimeout(() => this.expire(), delayMs);
  }

  private publishActivity(force: boolean): void {
    const now = this.clock();
    if (!force && now - this.lastWrite < ACTIVITY_WRITE_THROTTLE_MS) {
      return;
    }
    this.lastWrite = now;
    this.storage.write(StorageKeys.LastActivity, String(now));
  }

  /** How long another tab's later activity still keeps the session alive; 0 when none does. */
  private sharedActivityRemainingMs(): number {
    const last = Number(this.storage.read(StorageKeys.LastActivity)) || 0;
    if (last <= this.lastWrite) {
      return 0;
    }
    return Math.max(0, last + this.timeoutMs + ACTIVITY_WRITE_THROTTLE_MS - this.clock());
  }

  private expire(): void {
    const remaining = this.sharedActivityRemainingMs();
    if (remaining > 0) {
      this.arm(remaining);
      return;
    }
    this.stopWatching();
    void this.auth.expireSession(SessionEndReasons.Idle, this.router.url);
  }

  private clearTimer(): void {
    if (this.timer !== undefined) {
      clearTimeout(this.timer);
      this.timer = undefined;
    }
  }
}
