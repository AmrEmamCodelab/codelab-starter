import type { Routes } from '@angular/router';
import { rootLink } from '../../layouts/portal-shell/portal-nav';

/**
 * The notifications page's path inside the staff shell (`/notifications`). Every signed-in user may open it: the page
 * shows only the reader's own notifications and the API answers it for any authenticated user, so it carries no
 * permission key (the shell's `authGuard` applies).
 */
export const NOTIFICATIONS_ROUTE_PATH = 'notifications';

/** The absolute router link of the page (what the bell's "view all" opens). */
export const NOTIFICATIONS_LINK = rootLink(NOTIFICATIONS_ROUTE_PATH);

/** The notifications page, mounted by the app as a child of the staff shell (behind `authGuard`). */
export const NOTIFICATIONS_ROUTES: Routes = [
  {
    path: NOTIFICATIONS_ROUTE_PATH,
    loadComponent: () =>
      import('./notifications-page/notifications-page.component').then(m => m.NotificationsPageComponent),
  },
];
