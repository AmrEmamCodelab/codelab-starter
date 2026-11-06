import type { Routes } from '@angular/router';
import { NOTIFICATIONS_ROUTES } from './features/notifications/notifications.routes';
import { SERVICE_REQUESTS_REQUESTER_ROUTES } from './features/service-requests/service-requests.routes';// codelab:module ServiceRequestsArchive

export const SHELL_CHILDREN: Routes = [
  ...NOTIFICATIONS_ROUTES,
  ...SERVICE_REQUESTS_REQUESTER_ROUTES, // codelab:module ServiceRequestsArchive
];

// The word codelab:module ServiceRequests outside a comment is not a marker.
export const NOTE = 'codelab:module ServiceRequests';
