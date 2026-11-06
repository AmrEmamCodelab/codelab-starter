import type { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { unsavedChangesGuard } from '../access/common/unsaved-changes.guard';
import { PortalContentPermissionKeys } from './portal-content-permissions';
import { PORTAL_CONTENT_MOUNT_SEGMENT } from './portal-content.paths';

/**
 * The portal content editor, spread into `ADMIN_ROUTES` (`...PORTAL_CONTENT_ROUTES`): gated by `portal.content.view`
 * (saving needs `portal.content.manage`), and it asks before unsaved edits are left.
 */
export const PORTAL_CONTENT_ROUTES: Routes = [
  {
    path: PORTAL_CONTENT_MOUNT_SEGMENT,
    canActivate: [permissionGuard()],
    canDeactivate: [unsavedChangesGuard],
    data: { [RouteDataKeys.Permission]: PortalContentPermissionKeys.View },
    loadComponent: () =>
      import('./portal-content-page.component').then((m) => m.PortalContentPageComponent),
  },
];
