import { inject } from '@angular/core';
import { Router, type ActivatedRouteSnapshot, type CanActivateFn } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { ErrorDialogStore } from '../http/error-dialog.store';
import { PermissionStore } from '../permissions/permission.store';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { RouteDataKeys } from '../routing/route-paths';
import { loginTree, unauthorizedTree } from '../routing/route-trees';
import { AuthService } from './auth.service';

/** The permission a route requires: the guard argument, else the route's `data.permission`. */
function requiredPermission(route: ActivatedRouteSnapshot, key: string | undefined): string | null {
  if (key) {
    return key;
  }
  const fromData: unknown = route.data[RouteDataKeys.Permission];
  return typeof fromData === 'string' && fromData !== '' ? fromData : null;
}

/**
 * Lets a user through only when they hold the permission: `permissionGuard(PermissionKeys.AdminView)`,
 * or `permissionGuard()` with `data: { permission: PermissionKeys.AdminView }` on the route.
 *
 * Fails closed: a route that names no permission, or a permission sync that fails, is refused. A
 * signed-out user goes to the login page; a signed-in user without the key goes to the 403 page. A sync
 * that fails (a server fault, not a missing key) is not shown as a 403: the navigation is cancelled and the
 * error dialog shows `errors.<CODE>` with the reference code; navigating again retries the sync. A 401
 * there is left to the session-expiry interceptor.
 */
export function permissionGuard(key?: string): CanActivateFn {
  return async (route, state) => {
    const router = inject(Router);
    const store = inject(PermissionStore);
    const sync = inject(PermissionSyncService);
    const dialog = inject(ErrorDialogStore);
    if (!inject(AuthService).isAuthenticated()) {
      return loginTree(router, state.url);
    }
    const required = requiredPermission(route, key);
    const loaded = await sync.ensureLoaded();
    const failure = loaded ? null : sync.lastFailure();
    if (failure) {
      if (failure.status !== HttpStatus.unauthorized) {
        dialog.show(failure);
      }
      return false;
    }
    return required !== null && loaded && store.has(required) ? true : unauthorizedTree(router);
  };
}
