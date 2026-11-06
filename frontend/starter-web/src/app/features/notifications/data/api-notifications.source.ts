import { DestroyRef, Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom, interval, type Subscription } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { quietContext } from '../../../core/http/http-context';
import { LanguageService } from '../../../core/i18n/language.service';
import type { NotificationsSource } from '../../../layouts/notifications-menu/notifications';
import {
  EMPTY_FEED,
  UNREAD_POLL_INTERVAL_MS,
  appendOlder,
  feedFromPage,
  markReadIn,
  mergeNewest,
  type NotificationFeed,
} from './notifications-feed';
import { NotificationsClient, type NotificationPageDto } from './notifications.client';

/** Where the newest page stands. Integers, like every enum the starter owns. */
export enum NotificationsLoadStatus {
  /** No session: nothing is loaded or polled. */
  SignedOut = 1,
  Loading = 2,
  Loaded = 3,
  /** The newest page could not be loaded; {@link ApiNotificationsSource.refresh} tries again. */
  Failed = 4,
}

/**
 * The API-backed `NotificationsSource` (`NOTIFICATIONS_SOURCE` in the app): the signed-in reader's notifications from
 * `GET api/notifications`, newest first, paged by cursor. It sends no
 * page size, so each page holds as many as the server's `notifications.page-size` setting says.
 *
 * - Follows the session and the language: signing in (or switching the language, since the server writes the texts
 *   in the language asked for) loads the newest page; signing out clears everything and stops polling.
 * - While signed in it polls `GET api/notifications/unread-count` every {@link UNREAD_POLL_INTERVAL_MS}; when the
 *   count changes it merges the newest page in, so a new notification reaches the bell without a reload.
 * - `markRead` and `loadMore` never reject (the `NotificationsSource` contract): a refusal is reported by the app's
 *   HTTP error interceptor and leaves the state as it was. Background calls (first page, poll) are quiet: the first
 *   page's failure shows as {@link status} `Failed` on the feed page; a failed poll waits for the next tick.
 */
@Injectable({ providedIn: 'root' })
export class ApiNotificationsSource implements NotificationsSource {
  private readonly client = inject(NotificationsClient);
  private readonly auth = inject(AuthService);
  private readonly language = inject(LanguageService);
  private readonly feed = signal<NotificationFeed>(EMPTY_FEED);
  private readonly unread = signal(0);
  private readonly loadStatus = signal(NotificationsLoadStatus.SignedOut);
  private readonly fetchingOlder = signal(false);
  private poll: Subscription | null = null;
  /** Bumped on every sign-in, sign-out and language switch, so an answer for an older session or language is dropped. */
  private generation = 0;

  readonly items = computed(() => this.feed().items);
  readonly hasMore = computed(() => this.feed().hasMore);
  /** The server's unread total (every unread notification, loaded or not). */
  readonly unreadCount = this.unread.asReadonly();
  readonly status = this.loadStatus.asReadonly();
  /** True while an older page is loading (the feed's "load more" shows busy). */
  readonly loadingMore = this.fetchingOlder.asReadonly();

  constructor() {
    effect(() => {
      const signedIn = this.auth.isAuthenticated();
      // Read for tracking only: a language switch reloads the texts, which the server writes in the asked language.
      this.language.current();
      untracked(() => (signedIn ? this.start() : this.stop()));
    });
    inject(DestroyRef).onDestroy(() => this.stopPolling());
  }

  /** Loads the newest page again (the feed page's retry and its first render). Never rejects. */
  refresh(): Promise<void> {
    return this.auth.isAuthenticated() ? this.loadNewest(this.generation) : Promise.resolve();
  }

  async markRead(id: string): Promise<void> {
    const item = this.feed().items.find(candidate => candidate.id === id);
    if (item === undefined || item.read) {
      return;
    }
    const generation = this.generation;
    try {
      await firstValueFrom(this.client.markRead(id));
    } catch {
      // Refused: the HTTP error interceptor told the reader; the notification stays unread (contract: never reject).
      return;
    }
    if (generation === this.generation) {
      this.feed.update(feed => markReadIn(feed, id));
      this.unread.update(count => Math.max(0, count - 1));
    }
  }

  async loadMore(): Promise<void> {
    const oldest = this.feed().items.at(-1);
    if (!this.feed().hasMore || oldest === undefined || this.fetchingOlder()) {
      return;
    }
    const generation = this.generation;
    this.fetchingOlder.set(true);
    try {
      const page = await this.fetchPage({ before: oldest.id, quiet: false });
      if (generation === this.generation) {
        this.feed.update(feed => appendOlder(feed, page));
      }
    } catch {
      // Refused: the HTTP error interceptor told the reader; the list stays as it was (contract: never reject).
    } finally {
      this.fetchingOlder.set(false);
    }
  }

  private start(): void {
    this.reset(NotificationsLoadStatus.Loading);
    void this.loadNewest(this.generation);
    this.poll = interval(UNREAD_POLL_INTERVAL_MS).subscribe(() => void this.checkUnread(this.generation));
  }

  private stop(): void {
    this.reset(NotificationsLoadStatus.SignedOut);
  }

  private reset(status: NotificationsLoadStatus): void {
    this.stopPolling();
    this.generation++;
    this.feed.set(EMPTY_FEED);
    this.unread.set(0);
    this.fetchingOlder.set(false);
    this.loadStatus.set(status);
  }

  private stopPolling(): void {
    this.poll?.unsubscribe();
    this.poll = null;
  }

  private async loadNewest(generation: number): Promise<void> {
    this.loadStatus.set(NotificationsLoadStatus.Loading);
    try {
      const [page, unread] = await Promise.all([
        this.fetchPage({ before: null, quiet: true }),
        firstValueFrom(this.client.unreadCount(quietContext())),
      ]);
      if (generation === this.generation) {
        this.feed.set(feedFromPage(page));
        this.unread.set(unread.count);
        this.loadStatus.set(NotificationsLoadStatus.Loaded);
      }
    } catch {
      // Quiet background call: the feed page shows the failure with a retry; the bell keeps what it had.
      if (generation === this.generation) {
        this.loadStatus.set(NotificationsLoadStatus.Failed);
      }
    }
  }

  /** The poll tick: when the unread count moved, merge the newest page in. A failed tick waits for the next one. */
  private async checkUnread(generation: number): Promise<void> {
    try {
      const { count } = await firstValueFrom(this.client.unreadCount(quietContext()));
      if (generation !== this.generation || count === this.unread()) {
        return;
      }
      const page = await this.fetchPage({ before: null, quiet: true });
      if (generation === this.generation) {
        this.unread.set(count);
        this.feed.update(feed => mergeNewest(feed, page));
        this.loadStatus.set(NotificationsLoadStatus.Loaded);
      }
    } catch {
      // Best effort: polling is a background refresh; the next tick tries again and the reader's state is unchanged.
    }
  }

  /**
   * One page in the active language, older than `before` (null: the newest). A quiet call skips the error dialog; a
   * reader-triggered one lets the interceptor report a refusal.
   */
  private fetchPage(request: { readonly before: string | null; readonly quiet: boolean }): Promise<NotificationPageDto> {
    return firstValueFrom(
      this.client.page({
        before: request.before,
        language: this.language.current(),
        context: request.quiet ? quietContext() : undefined,
      }),
    );
  }
}
