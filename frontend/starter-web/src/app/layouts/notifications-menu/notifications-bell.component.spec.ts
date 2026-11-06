import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { ANY_URL_ROUTES, aNotification, BOTH_DIRECTIONS, fakeAuth, layoutTestProviders, useDirection } from '../layout.testing';
import { NOTIFICATIONS_FEED_LINK, NOTIFICATIONS_SOURCE, type NotificationItem, type NotificationsSource } from './notifications';
import { NotificationsBellComponent } from './notifications-bell.component';

const FEED_LINK = '/notifications';

function source(items: readonly NotificationItem[], unreadCount?: number) {
  return {
    items: signal(items),
    hasMore: signal(false),
    ...(unreadCount === undefined ? {} : { unreadCount: signal(unreadCount) }),
    markRead: vi.fn<(id: string) => Promise<void>>().mockResolvedValue(undefined),
    loadMore: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
  } satisfies NotificationsSource;
}

function render(notifications: NotificationsSource, feedLink: string | null = FEED_LINK): HTMLElement {
  TestBed.configureTestingModule({
    imports: [NotificationsBellComponent],
    providers: [
      provideRouter(ANY_URL_ROUTES),
      ...layoutTestProviders(fakeAuth()),
      { provide: NOTIFICATIONS_SOURCE, useValue: notifications },
      { provide: NOTIFICATIONS_FEED_LINK, useValue: feedLink },
    ],
  });
  const fixture = TestBed.createComponent(NotificationsBellComponent);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe.each(BOTH_DIRECTIONS)('NotificationsBellComponent (%s)', direction => {
  it('shows the source items and the server unread total, and links to the feed page', () => {
    const restore = useDirection(direction);
    const host = render(source([aNotification({ id: 'n-1' })], 7));

    expect(host.querySelector('.notifications__badge')?.textContent?.trim()).toBe('7');
    (host.querySelector('.notifications__bell') as HTMLButtonElement).click();
    TestBed.tick();
    expect(host.querySelector('.notifications__view-all')?.getAttribute('href')).toBe(FEED_LINK);
    restore();
  });

  it('counts the unread items when the source has no unread total', () => {
    const restore = useDirection(direction);
    const host = render(source([aNotification({ id: 'n-1' }), aNotification({ id: 'n-2', read: true })]), null);
    expect(host.querySelector('.notifications__badge')?.textContent?.trim()).toBe('1');
    restore();
  });

  it('forwards mark-read to the source', () => {
    const restore = useDirection(direction);
    const notifications = source([aNotification({ id: 'n-9' })]);
    const host = render(notifications);
    (host.querySelector('.notifications__bell') as HTMLButtonElement).click();
    TestBed.tick();
    (host.querySelector('.notifications__mark') as HTMLButtonElement).click();
    expect(notifications.markRead).toHaveBeenCalledWith('n-9');
    restore();
  });
});
