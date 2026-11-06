import { inject } from '@angular/core';
import { Router, type CanActivateFn, type CanMatchFn } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { holdsPermissions, type PermissionRequirement } from '../../../core/permissions/permission-match';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { PermissionSyncService } from '../../../core/permissions/permission-sync.service';
import { SERVICE_REQUESTS_REQUESTER_BASE_LINK, SERVICE_REQUESTS_REQUESTER_PATH } from './requester-paths';
import { RequesterGates } from './requester-permissions';

/**
 * `canMatch` of the requester shell mounted at the app root: the shell claims a URL only when it opens the requester
 * area (`/service-requests/...`). Without it the empty-path shell also claims `/` (the router lets an empty-path
 * layout match with no child left), rendering an empty requester frame instead of reaching the staff dashboard.
 */
export const requesterShellMatch: CanMatchFn = (_route, segments) =>
  segments[0]?.path === SERVICE_REQUESTS_REQUESTER_PATH;

/**
 * Runs before `permissionGuard()` on the staff dashboard (`/`): a requester who holds no `dashboard.view` but holds
 * `requesterGate` is sent to the requester dashboard instead of the 403 page. Everyone else passes through, so the
 * dashboard's own `permissionGuard()` still decides (staff and admins land on the staff dashboard; a signed-out user,
 * a failed sync or a user with neither key is handled there).
 */
export function requesterLanding(requesterGate: PermissionRequirement): CanActivateFn {
  return async () => {
    if (!inject(AuthService).isAuthenticated()) {
      return true;
    }
    // Every inject() runs before the await: the injection context does not survive it.
    const store = inject(PermissionStore);
    const sync = inject(PermissionSyncService);
    const router = inject(Router);
    const link = inject(SERVICE_REQUESTS_REQUESTER_BASE_LINK);
    if (!(await sync.ensureLoaded()) || store.has(PermissionKeys.DashboardView)) {
      return true;
    }
    return holdsPermissions((held) => store.has(held), requesterGate) ? router.parseUrl(link) : true;
  };
}

/** {@link requesterLanding} on `service-requests.view-own` (fails closed while the key is not generated yet). */
export const requesterLandingGuard: CanActivateFn = requesterLanding(RequesterGates.ViewOwn);
