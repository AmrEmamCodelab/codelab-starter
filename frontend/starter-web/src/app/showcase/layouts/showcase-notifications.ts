import { computed, inject, signal, type Provider, type Signal } from '@angular/core';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK, type Clock } from '../../core/session/clock';
import { NOTIFICATIONS_SOURCE, type NotificationItem, type NotificationsSource } from '../../layouts/notifications-menu/notifications';
import { hoursFromNow } from '../showcase-clock';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import type { Translate } from '../components/component-fixtures';

const L = ShowcaseTranslationKeys.Layouts;

/** One fixture notification: texts by key, its age in hours before the showcase clock. */
export interface NotificationFixture {
  readonly id: string;
  readonly titleKey: string;
  readonly bodyKey: string;
  readonly hoursAgo: number;
  readonly read: boolean;
  readonly link?: string;
}

/** The first page of notifications (newest first). */
export const NOTIFICATIONS_FIRST_PAGE: readonly NotificationFixture[] = [
  { id: 'n-returned', titleKey: L.NoticeReturnedTitle, bodyKey: L.NoticeReturnedBody, hoursAgo: 2, read: false, link: showcaseLink(ShowcasePageId.RequesterDetail) },
  { id: 'n-approved', titleKey: L.NoticeApprovedTitle, bodyKey: L.NoticeApprovedBody, hoursAgo: 26, read: false },
  { id: 'n-received', titleKey: L.NoticeReceivedTitle, bodyKey: L.NoticeReceivedBody, hoursAgo: 75, read: true },
];

/** The older page "load more" appends. */
export const NOTIFICATIONS_OLDER_PAGE: readonly NotificationFixture[] = [
  { id: 'n-maintenance', titleKey: L.NoticeMaintenanceTitle, bodyKey: L.NoticeMaintenanceBody, hoursAgo: 170, read: true },
];

/**
 * An in-memory {@link NotificationsSource} for the showcase: two pages of fixture notifications in the reader's
 * language, timed against the showcase clock. `markRead` and `loadMore` change only this instance, never reject and
 * call no API (the real source arrives in P5).
 */
export class ShowcaseNotificationsSource implements NotificationsSource {
  private readonly loaded = signal<readonly NotificationFixture[]>(NOTIFICATIONS_FIRST_PAGE);
  private readonly readIds = signal<ReadonlySet<string>>(new Set());

  readonly items: Signal<readonly NotificationItem[]>;
  readonly hasMore = computed(() => this.loaded().length < NOTIFICATIONS_FIRST_PAGE.length + NOTIFICATIONS_OLDER_PAGE.length);

  constructor(translate: Translate, clock: Clock) {
    this.items = computed(() =>
      this.loaded().map(entry => ({
        id: entry.id,
        titleText: translate(entry.titleKey),
        bodyText: translate(entry.bodyKey),
        at: hoursFromNow(clock, -entry.hoursAgo),
        read: entry.read || this.readIds().has(entry.id),
        ...(entry.link === undefined ? {} : { link: entry.link }),
      })),
    );
  }

  markRead(id: string): Promise<void> {
    this.readIds.update(ids => new Set([...ids, id]));
    return Promise.resolve();
  }

  loadMore(): Promise<void> {
    if (this.hasMore()) {
      this.loaded.update(entries => [...entries, ...NOTIFICATIONS_OLDER_PAGE]);
    }
    return Promise.resolve();
  }
}

/** Provides a {@link ShowcaseNotificationsSource} as the `NOTIFICATIONS_SOURCE` of a showcase page's subtree. */
export function provideShowcaseNotifications(): Provider {
  return {
    provide: NOTIFICATIONS_SOURCE,
    useFactory: () => {
      const translations = inject(TranslationService);
      return new ShowcaseNotificationsSource(key => translations.translate(key), inject(APP_CLOCK));
    },
  };
}
