import type { Provider } from '@angular/core';
import { NOTIFICATIONS_FEED_LINK, NOTIFICATIONS_SOURCE } from '../../layouts/notifications-menu/notifications';
import { ApiNotificationsSource } from './data/api-notifications.source';
import { NOTIFICATIONS_LINK } from './notifications.routes';

/**
 * The app-wide notifications wiring for `app.config.ts`: the API-backed source as `NOTIFICATIONS_SOURCE` (one
 * instance, shared by every bell and the feed page) and the feed page as the bell's "view all" link.
 */
export function provideApiNotifications(): Provider[] {
  return [
    { provide: NOTIFICATIONS_SOURCE, useExisting: ApiNotificationsSource },
    { provide: NOTIFICATIONS_FEED_LINK, useValue: NOTIFICATIONS_LINK },
  ];
}
