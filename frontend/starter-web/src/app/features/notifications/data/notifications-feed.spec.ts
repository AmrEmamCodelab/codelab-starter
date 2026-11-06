import { describe, expect, it } from 'vitest';
import {
  EMPTY_FEED,
  appendOlder,
  feedFromPage,
  markReadIn,
  mergeNewest,
  toNotificationItem,
  type NotificationFeed,
} from './notifications-feed';
import type { NotificationDto, NotificationPageDto } from './notifications.client';

function dto(id: string, overrides: Partial<NotificationDto> = {}): NotificationDto {
  return { id, titleText: `Title ${id}`, bodyText: 'Body', at: '2026-11-05T09:30:00Z', read: false, link: null, ...overrides };
}

const page = (ids: readonly string[], hasMore: boolean, overrides: Partial<NotificationDto> = {}): NotificationPageDto => ({
  items: ids.map(id => dto(id, overrides)),
  hasMore,
});

const ids = (feed: NotificationFeed): string[] => feed.items.map(item => item.id);

describe('toNotificationItem', () => {
  it('keeps the texts and drops a null or empty link', () => {
    expect(toNotificationItem(dto('n-1'))).toEqual({ id: 'n-1', titleText: 'Title n-1', bodyText: 'Body', at: '2026-11-05T09:30:00Z', read: false });
    expect(toNotificationItem(dto('n-1', { link: '' })).link).toBeUndefined();
    expect(toNotificationItem(dto('n-1', { link: '/requests/r-1' })).link).toBe('/requests/r-1');
  });
});

describe('feed paging', () => {
  it('starts from the first page', () => {
    expect(feedFromPage(page(['n-3', 'n-2'], true))).toMatchObject({ hasMore: true });
    expect(ids(feedFromPage(page(['n-3', 'n-2'], true)))).toEqual(['n-3', 'n-2']);
  });

  it('appends an older page without repeating a listed notification', () => {
    const feed = appendOlder(feedFromPage(page(['n-4', 'n-3'], true)), page(['n-3', 'n-2', 'n-1'], false));
    expect(ids(feed)).toEqual(['n-4', 'n-3', 'n-2', 'n-1']);
    expect(feed.hasMore).toBe(false);
  });
});

describe('mergeNewest', () => {
  const loaded: NotificationFeed = appendOlder(feedFromPage(page(['n-4', 'n-3'], true)), page(['n-2', 'n-1'], true));

  it('puts new notifications first, keeps the older loaded ones and their paging', () => {
    const merged = mergeNewest(loaded, page(['n-6', 'n-5', 'n-4'], true, { read: true }));
    expect(ids(merged)).toEqual(['n-6', 'n-5', 'n-4', 'n-3', 'n-2', 'n-1']);
    expect(merged.items.find(item => item.id === 'n-4')?.read).toBe(true);
    expect(merged.hasMore).toBe(true);
  });

  it('restarts from the page when it shares nothing with the feed (a gap) or is the whole list', () => {
    expect(ids(mergeNewest(loaded, page(['n-9', 'n-8'], true)))).toEqual(['n-9', 'n-8']);
    const whole = mergeNewest(loaded, page(['n-5', 'n-4'], false));
    expect(ids(whole)).toEqual(['n-5', 'n-4']);
    expect(whole.hasMore).toBe(false);
    expect(ids(mergeNewest(EMPTY_FEED, page(['n-1'], false)))).toEqual(['n-1']);
  });
});

describe('markReadIn', () => {
  it('marks only the given notification read', () => {
    const feed = markReadIn(feedFromPage(page(['n-2', 'n-1'], false)), 'n-1');
    expect(feed.items.map(item => item.read)).toEqual([false, true]);
    expect(markReadIn(feed, 'missing').items).toEqual(feed.items);
  });
});
