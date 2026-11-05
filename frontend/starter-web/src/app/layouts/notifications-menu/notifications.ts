import { InjectionToken, signal, type Signal } from '@angular/core';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';

/**
 * One in-app notification as the layouts render it. The texts arrive already in the reader's language (the source
 * resolves them); the layouts add no wording of their own to a notification.
 */
export interface NotificationItem {
  /** Stable identifier (the server's public id); unique in a list. */
  readonly id: string;
  readonly titleText: string;
  readonly bodyText: string;
  /** When it was raised, as an ISO 8601 date-time (`2026-11-05T09:30:00Z`). */
  readonly at: string;
  readonly read: boolean;
  /** Absolute in-app router link the notification opens (`/requests/abc`); none renders it as plain text. */
  readonly link?: string;
}

/**
 * Where the layouts read notifications from and send the reader's intent to. P4 ships only the empty default
 * ({@link EmptyNotificationsSource}); P5 provides the real one (API-backed) under {@link NOTIFICATIONS_SOURCE}.
 *
 * Contract: `items` is newest first and holds every page loaded so far; `markRead` and `loadMore` update `items`
 * and `hasMore` themselves once the server agreed. They never reject: a refusal is reported to the user by the app's
 * error handling (the HTTP error interceptor) and leaves the state as it was, so the layouts call them fire-and-forget.
 */
export interface NotificationsSource {
  readonly items: Signal<readonly NotificationItem[]>;
  /** True while older notifications remain to load. */
  readonly hasMore: Signal<boolean>;
  markRead(id: string): Promise<void>;
  /** Appends the next page of older notifications. */
  loadMore(): Promise<void>;
}

/** The P4 default: no notifications, nothing more to load, every intent a no-op. */
export class EmptyNotificationsSource implements NotificationsSource {
  readonly items = signal<readonly NotificationItem[]>([]).asReadonly();
  readonly hasMore = signal(false).asReadonly();

  markRead(): Promise<void> {
    return Promise.resolve();
  }

  loadMore(): Promise<void> {
    return Promise.resolve();
  }
}

/** The notifications the shells show. Defaults to {@link EmptyNotificationsSource}; P5 overrides it in `app.config.ts`. */
export const NOTIFICATIONS_SOURCE = new InjectionToken<NotificationsSource>('NOTIFICATIONS_SOURCE', {
  providedIn: 'root',
  factory: () => new EmptyNotificationsSource(),
});

/** The largest unread count the badge spells out; above it the badge reads `99+`. */
export const MAX_BADGE_COUNT = 99;
const BADGE_OVERFLOW_SUFFIX = '+';

/** The notifications the bell dropdown lists at most; the rest are on the feed page behind "view all". */
export const MENU_ITEM_LIMIT = 5;

/** How many of `items` are unread. */
export function unreadCount(items: readonly NotificationItem[]): number {
  return items.reduce((count, item) => (item.read ? count : count + 1), 0);
}

/** The badge text for `count` unread notifications: empty for none, `99+` above {@link MAX_BADGE_COUNT}. */
export function badgeText(count: number): string {
  if (count <= 0) {
    return '';
  }
  return count > MAX_BADGE_COUNT ? `${MAX_BADGE_COUNT}${BADGE_OVERFLOW_SUFFIX}` : String(count);
}

/** The feed filters: every notification, the unread ones or the read ones. Integers, like every enum the starter owns. */
export enum NotificationFilter {
  All = 1,
  Unread = 2,
  Read = 3,
}

/** The notifications `filter` keeps, in their original order. */
export function filterNotifications(
  items: readonly NotificationItem[],
  filter: NotificationFilter,
): readonly NotificationItem[] {
  switch (filter) {
    case NotificationFilter.Unread:
      return items.filter(item => !item.read);
    case NotificationFilter.Read:
      return items.filter(item => item.read);
    default:
      return items;
  }
}

/** The locale each app language formats dates in: Gregorian calendar, Latin digits (the DGA content convention). */
const DATE_LOCALES: Readonly<Record<AppLanguage, string>> = {
  [AppLanguages.Arabic]: 'ar-SA-u-ca-gregory-nu-latn',
  [AppLanguages.English]: 'en-GB',
};

/** The layouts show times and the copyright year in Saudi time, whatever the browser's zone, so every reader agrees. */
export const LAYOUT_TIME_ZONE = 'Asia/Riyadh';

const TIME_FORMAT: Intl.DateTimeFormatOptions = {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: LAYOUT_TIME_ZONE,
};

/** `at` (ISO 8601) as a date and time in `language`; an unparseable value is returned as written. */
export function formatNotificationTime(at: string, language: AppLanguage): string {
  const time = new Date(at);
  if (Number.isNaN(time.getTime())) {
    return at;
  }
  return new Intl.DateTimeFormat(DATE_LOCALES[language], TIME_FORMAT).format(time);
}
