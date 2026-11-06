import type { Routes } from '@angular/router';
import { permissionGuard } from '../../core/auth/permission.guard';
import { RouteDataKeys } from '../../core/routing/route-paths';
import { rootLink } from '../../layouts/portal-shell/portal-nav';
import { AttachmentsPermissionKeys } from '../../shared/record-attachments/attachments-permission-keys';
import { attachmentsDemoEnabledGuard } from './attachments-demo-enabled.guard';

/** The attachments demo page's path inside the staff shell (`/attachments/demo`, development builds only). */
export const ATTACHMENTS_DEMO_ROUTE_PATH = 'attachments/demo';

/** The absolute router link of the demo page. */
export const ATTACHMENTS_DEMO_LINK = rootLink(ATTACHMENTS_DEMO_ROUTE_PATH);

/**
 * The attachments pages, mounted by the app as children of the staff shell (behind `authGuard`). The demo page matches
 * only in a development build ({@link attachmentsDemoEnabledGuard}) and needs `attachments.view`.
 */
export const ATTACHMENTS_ROUTES: Routes = [
  {
    path: ATTACHMENTS_DEMO_ROUTE_PATH,
    canMatch: [attachmentsDemoEnabledGuard],
    canActivate: [permissionGuard()],
    data: { [RouteDataKeys.Permission]: AttachmentsPermissionKeys.View },
    loadComponent: () =>
      import('./demo-page/attachments-demo-page.component').then(m => m.AttachmentsDemoPageComponent),
  },
];
