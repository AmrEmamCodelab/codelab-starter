import { HttpErrorResponse } from '@angular/common/http';
import { DOCUMENT, DestroyRef, Injectable, Injector, effect, inject, untracked } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { HttpStatus, parseApiError, type ApiError } from '@codelab/ng-security';
import { filter, firstValueFrom, type Subscription } from 'rxjs';
import { MeClient } from '../api/clients/me.client';
import { TokenStore } from '../auth/token.store';
import { AppConfigService } from '../config/app-config.service';
import { APP_CLOCK } from '../session/clock';
import { MILLISECONDS_PER_SECOND } from '../session/time-units';
import { PermissionStore } from './permission.store';

/** A sync failure that is not an HTTP response, read as a server fault. */
const NON_HTTP_FAILURE = { status: HttpStatus.serverError } as const;

/** Seconds a focus, visibility or navigation trigger waits after the previous sync (see PERMISSION_SYNC_THROTTLE_MS). */
const PERMISSION_SYNC_THROTTLE_SECONDS = 10;

/**
 * The least time between two event-triggered syncs. A burst (a redirect chain, focus and visibility firing together,
 * fast clicking through the menu) collapses into one request; the interval is not throttled.
 */
export const PERMISSION_SYNC_THROTTLE_MS = PERMISSION_SYNC_THROTTLE_SECONDS * MILLISECONDS_PER_SECOND;

/** The window events that mean "the user may have come back after an admin changed something". */
const FOCUS_EVENT = 'focus';
const VISIBILITY_EVENT = 'visibilitychange';
const VISIBLE_STATE: DocumentVisibilityState = 'visible';

/**
 * Keeps {@link PermissionStore} in step with the server. Grants and revocations take effect without a new login:
 * the server resolves the user's keys live on every call, and this service repaints what the browser shows.
 *
 * - {@link refresh} runs at login, on start-up with a restored session, and after any 403. Concurrent refreshes
 *   share one request.
 * - {@link start} (called once by `initializeCore`) adds the live triggers while a session exists: window focus, the
 *   tab becoming visible and `NavigationEnd` (each throttled by {@link PERMISSION_SYNC_THROTTLE_MS}), and an
 *   interval of `config.json` `permissionRefreshSeconds`. The triggers stop when the session ends.
 *
 * A background sync is quiet: its failure keeps the previous keys and shows no dialog. A deactivated user's next
 * sync answers 401, which the session-expiry interceptor turns into a signed-out session.
 */
@Injectable({ providedIn: 'root' })
export class PermissionSyncService {
  private readonly client = inject(MeClient);
  private readonly store = inject(PermissionStore);
  private readonly tokens = inject(TokenStore);
  private readonly clock = inject(APP_CLOCK);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);
  private pendingSync: Promise<void> | null = null;
  private failure: ApiError | null = null;
  private started = false;
  private watching = false;
  private lastSyncAt = Number.NEGATIVE_INFINITY;
  private interval: ReturnType<typeof setInterval> | undefined;
  private navigation: Subscription | undefined;

  private readonly onFocus = (): void => this.syncSoon();
  private readonly onVisibility = (): void => {
    if (this.document.visibilityState === VISIBLE_STATE) {
      this.syncSoon();
    }
  };

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopWatching());
  }

  /**
   * Why the last {@link ensureLoaded} could not load the keys (the parsed server fault, with its reference
   * code), or null when it loaded. Lets a guard tell "sync failed" apart from "permission missing".
   */
  lastFailure(): ApiError | null {
    return this.failure;
  }

  /**
   * Fetches the user's keys into the store.
   *
   * @throws the HTTP failure when the keys could not be fetched; the store keeps its previous keys.
   */
  refresh(): Promise<void> {
    this.lastSyncAt = this.clock();
    this.pendingSync ??= firstValueFrom(this.client.permissions())
      .then(response => this.store.set(response.permissions))
      .finally(() => {
        this.pendingSync = null;
      });
    return this.pendingSync;
  }

  /**
   * Resolves once the store holds this session's keys. A failed sync resolves too, leaving the store
   * unloaded, so a guard denies rather than hangs.
   */
  async ensureLoaded(): Promise<boolean> {
    if (this.store.loaded()) {
      this.failure = null;
      return true;
    }
    try {
      await this.refresh();
    } catch (error: unknown) {
      this.failure = parseApiError(error instanceof HttpErrorResponse ? error : NON_HTTP_FAILURE);
      return false;
    }
    this.failure = null;
    return this.store.loaded();
  }

  clear(): void {
    this.failure = null;
    this.store.clear();
  }

  /** Follows the session: the live triggers run while signed in and stop when signed out. Idempotent. */
  start(): void {
    if (this.started) {
      return;
    }
    this.started = true;
    effect(
      () => {
        const signedIn = this.tokens.session() !== null;
        untracked(() => (signedIn ? this.startWatching() : this.stopWatching()));
      },
      { injector: this.injector },
    );
  }

  private startWatching(): void {
    if (this.watching) {
      return;
    }
    this.watching = true;
    this.document.defaultView?.addEventListener(FOCUS_EVENT, this.onFocus);
    this.document.addEventListener(VISIBILITY_EVENT, this.onVisibility);
    this.navigation = this.injector
      .get(Router)
      .events.pipe(filter(event => event instanceof NavigationEnd))
      .subscribe(() => this.syncSoon());
    const seconds = this.injector.get(AppConfigService).permissionRefreshSeconds;
    if (seconds > 0) {
      this.interval = setInterval(() => this.syncInBackground(), seconds * MILLISECONDS_PER_SECOND);
    }
  }

  private stopWatching(): void {
    if (!this.watching) {
      return;
    }
    this.watching = false;
    this.document.defaultView?.removeEventListener(FOCUS_EVENT, this.onFocus);
    this.document.removeEventListener(VISIBILITY_EVENT, this.onVisibility);
    this.navigation?.unsubscribe();
    this.navigation = undefined;
    if (this.interval !== undefined) {
      clearInterval(this.interval);
      this.interval = undefined;
    }
  }

  /** An event-triggered sync, skipped when the previous one is more recent than the throttle. */
  private syncSoon(): void {
    if (this.clock() - this.lastSyncAt < PERMISSION_SYNC_THROTTLE_MS) {
      return;
    }
    this.syncInBackground();
  }

  /** Syncs only a session whose keys were loaded once (until then the guards own the first sync). */
  private syncInBackground(): void {
    if (this.tokens.session() === null || !this.store.loaded()) {
      return;
    }
    this.refresh().catch(() => {
      // Quiet by design: the store keeps the previous keys and the next trigger retries.
    });
  }
}
