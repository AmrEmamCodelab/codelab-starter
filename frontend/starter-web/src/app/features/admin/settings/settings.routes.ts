import type { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import type { PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { SettingsPermissionKeys } from './settings-permissions';
import { SettingsRouteSegments } from './settings.paths';

function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The runtime settings screen, mounted under `/admin/settings` by `ADMIN_ROUTES` (`loadChildren`). The page is gated
 * by `settings.view`; editing a value needs `settings.manage` (the edit buttons and the dialog are gated in the page).
 */
export const SETTINGS_ROUTES: Routes = [
  {
    path: SettingsRouteSegments.List,
    pathMatch: 'full',
    canActivate: [permissionGuard()],
    data: requires(SettingsPermissionKeys.View),
    loadComponent: () => import('./settings-page.component').then((m) => m.SettingsPageComponent),
  },
];
