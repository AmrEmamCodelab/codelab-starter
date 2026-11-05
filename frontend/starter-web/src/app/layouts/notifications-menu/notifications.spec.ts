import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../core/i18n/language';
import { aNotification } from '../layout.testing';
import {
  badgeText,
  EmptyNotificationsSource,
  filterNotifications,
  formatNotificationTime,
  MAX_BADGE_COUNT,
  NotificationFilter,
  NOTIFICATIONS_SOURCE,
  unreadCount,
} from './notifications';

const UNREAD = aNotification({ id: 'n-1', read: false });
const READ = aNotification({ id: 'n-2', read: true });
const ALSO_UNREAD = aNotification({ id: 'n-3', read: false });
const ITEMS = [UNREAD, READ, ALSO_UNREAD];

describe('unreadCount', () => {
  it('counts the unread notifications', () => {
    expect(unreadCount(ITEMS)).toBe(2);
    expect(unreadCount([READ])).toBe(0);
    expect(unreadCount([])).toBe(0);
  });
});

describe('badgeText', () => {
  it('is empty for no unread notification', () => {
    expect(badgeText(0)).toBe('');
    expect(badgeText(-1)).toBe('');
  });

  it('spells out a count up to the cap and caps above it', () => {
    expect(badgeText(1)).toBe('1');
    expect(badgeText(MAX_BADGE_COUNT)).toBe(String(MAX_BADGE_COUNT));
    expect(badgeText(MAX_BADGE_COUNT + 1)).toBe(`${MAX_BADGE_COUNT}+`);
  });
});

describe('filterNotifications', () => {
  it('keeps everything for All', () => {
    expect(filterNotifications(ITEMS, NotificationFilter.All)).toEqual(ITEMS);
  });

  it('keeps the unread ones in order for Unread', () => {
    expect(filterNotifications(ITEMS, NotificationFilter.Unread)).toEqual([UNREAD, ALSO_UNREAD]);
  });

  it('keeps the read ones for Read', () => {
    expect(filterNotifications(ITEMS, NotificationFilter.Read)).toEqual([READ]);
  });

  it('stores the filters as explicit integers', () => {
    expect([NotificationFilter.All, NotificationFilter.Unread, NotificationFilter.Read]).toEqual([1, 2, 3]);
  });
});

describe('formatNotificationTime', () => {
  const AT = '2026-11-05T09:30:00Z';

  it('formats in Saudi time (UTC+3) in English', () => {
    const text = formatNotificationTime(AT, AppLanguages.English);
    expect(text).toContain('2026');
    expect(text).toContain('12:30');
  });

  it('formats Gregorian with Latin digits in Arabic', () => {
    const text = formatNotificationTime(AT, AppLanguages.Arabic);
    expect(text).toContain('2026');
    expect(text).toMatch(/12:30/);
  });

  it('returns an unparseable value as written', () => {
    expect(formatNotificationTime('not-a-date', AppLanguages.English)).toBe('not-a-date');
  });
});

describe('NOTIFICATIONS_SOURCE', () => {
  it('defaults to the empty source', async () => {
    const source = TestBed.inject(NOTIFICATIONS_SOURCE);
    expect(source).toBeInstanceOf(EmptyNotificationsSource);
    expect(source.items()).toEqual([]);
    expect(source.hasMore()).toBe(false);
    await expect(source.markRead('n-1')).resolves.toBeUndefined();
    await expect(source.loadMore()).resolves.toBeUndefined();
  });
});
