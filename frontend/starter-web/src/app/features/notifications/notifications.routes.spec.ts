import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import {
  NOTIFICATIONS_FEED_LINK,
  NOTIFICATIONS_SOURCE,
  type NotificationsSource,
} from '../../layouts/notifications-menu/notifications';
import { ApiNotificationsSource } from './data/api-notifications.source';
import { NotificationsPageComponent } from './notifications-page/notifications-page.component';
import { provideApiNotifications } from './notifications.providers';
import { NOTIFICATIONS_LINK, NOTIFICATIONS_ROUTE_PATH, NOTIFICATIONS_ROUTES } from './notifications.routes';
import { NOTIFICATIONS_TRANSLATION_KEYS } from './notifications-translation-keys';

describe('notifications routes and providers', () => {
  it('mounts the page lazily at /notifications', async () => {
    expect(NOTIFICATIONS_ROUTE_PATH).toBe('notifications');
    expect(NOTIFICATIONS_LINK).toBe('/notifications');
    const [route] = NOTIFICATIONS_ROUTES;
    expect(route?.path).toBe(NOTIFICATIONS_ROUTE_PATH);
    expect(await route?.loadComponent?.()).toBe(NotificationsPageComponent);
  });

  it('provides the API source as the app-wide notifications source, with the page as the feed link', () => {
    const fake: Partial<NotificationsSource> = {};
    TestBed.configureTestingModule({
      providers: [...provideApiNotifications(), { provide: ApiNotificationsSource, useValue: fake }],
    });
    expect(TestBed.inject(NOTIFICATIONS_SOURCE)).toBe(fake);
    expect(TestBed.inject(NOTIFICATIONS_FEED_LINK)).toBe(NOTIFICATIONS_LINK);
  });

  it('keys its texts under notifications.page', () => {
    for (const key of NOTIFICATIONS_TRANSLATION_KEYS) {
      expect(key).toMatch(/^notifications\.page\.[a-z-]+$/);
    }
  });
});
