import type { NotificationItem } from '../../../layouts/notifications-menu/notifications';
import type { NotificationDto, NotificationPageDto } from './notifications.client';

/** How often the unread count is polled while signed in, in milliseconds (one minute). */
export const UNREAD_POLL_INTERVAL_MS = 60_000;

/** The notifications loaded so far (newest first) and whether older ones remain. */
export interface NotificationFeed {
  readonly items: readonly NotificationItem[];
  readonly hasMore: boolean;
}

export const EMPTY_FEED: NotificationFeed = { items: [], hasMore: false };

/** A server notification as the layouts render it (`link: null` becomes no link). */
export function toNotificationItem(dto: NotificationDto): NotificationItem {
  const item: NotificationItem = { id: dto.id, titleText: dto.titleText, bodyText: dto.bodyText, at: dto.at, read: dto.read };
  return dto.link === null || dto.link === '' ? item : { ...item, link: dto.link };
}

/** The first page as the whole feed. */
export function feedFromPage(page: NotificationPageDto): NotificationFeed {
  return { items: page.items.map(toNotificationItem), hasMore: page.hasMore };
}

/** The feed with an older page appended; a notification already listed is not repeated. */
export function appendOlder(feed: NotificationFeed, page: NotificationPageDto): NotificationFeed {
  const known = new Set(feed.items.map(item => item.id));
  const older = page.items.map(toNotificationItem).filter(item => !known.has(item.id));
  return { items: [...feed.items, ...older], hasMore: page.hasMore };
}

/**
 * The feed refreshed with a fresh newest page: the page's notifications first (their read state wins), then the older
 * ones already loaded. When the page is the whole list (`hasMore` false), or shares no notification with the feed (a
 * gap: more arrived than one page holds), the feed restarts from the page and the reader loads older ones again.
 */
export function mergeNewest(feed: NotificationFeed, page: NotificationPageDto): NotificationFeed {
  const freshIds = new Set(page.items.map(item => item.id));
  const overlaps = feed.items.some(item => freshIds.has(item.id));
  if (!page.hasMore || !overlaps) {
    return feedFromPage(page);
  }
  const older = feed.items.filter(item => !freshIds.has(item.id));
  return { items: [...page.items.map(toNotificationItem), ...older], hasMore: feed.hasMore };
}

/** The feed with notification `id` marked read (unchanged when it is not listed). */
export function markReadIn(feed: NotificationFeed, id: string): NotificationFeed {
  return { ...feed, items: feed.items.map(item => (item.id === id ? { ...item, read: true } : item)) };
}
