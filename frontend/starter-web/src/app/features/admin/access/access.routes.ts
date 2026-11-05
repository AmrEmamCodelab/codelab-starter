import type { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import { PermissionKeys, type PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { AccessRouteSegments } from './access.paths';
import { unsavedChangesGuard } from './common/unsaved-changes.guard';

/** The empty path of the mount: it opens the roles list. */
const MOUNT_PATH = '';

function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The access administration screens, mounted under `/admin/access` by `ADMIN_ROUTES` (whose mount is open to a
 * holder of any admin page key). Each page is gated again by its own key; the editors ask before unsaved edits are left.
 */
export const ACCESS_ROUTES: Routes = [
  {
    path: MOUNT_PATH,
    pathMatch: 'full',
    data: requires(PermissionKeys.AccessRolesView),
    redirectTo: AccessRouteSegments.Roles,
  },
  {
    path: AccessRouteSegments.Roles,
    canActivate: [permissionGuard()],
    data: requires(PermissionKeys.AccessRolesView),
    loadComponent: () => import('./roles/role-list.page').then((m) => m.RoleListPageComponent),
  },
  {
    path: AccessRouteSegments.RoleDetail,
    canActivate: [permissionGuard()],
    canDeactivate: [unsavedChangesGuard],
    data: requires(PermissionKeys.AccessRolesView),
    loadComponent: () => import('./roles/role-editor.page').then((m) => m.RoleEditorPageComponent),
  },
  {
    path: AccessRouteSegments.UserGrants,
    canActivate: [permissionGuard()],
    canDeactivate: [unsavedChangesGuard],
    data: requires(PermissionKeys.AccessGrantsView),
    loadComponent: () => import('./grants/user-grants.page').then((m) => m.UserGrantsPageComponent),
  },
];
