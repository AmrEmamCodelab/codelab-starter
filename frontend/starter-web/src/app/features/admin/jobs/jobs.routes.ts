import type { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import type { PermissionKey } from '../../../core/permissions/permission-keys.generated';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { JobsPermissionKeys } from './jobs-permissions';
import { JobsRouteSegments } from './jobs.paths';

function requires(permission: PermissionKey): Readonly<Record<string, PermissionKey>> {
  return { [RouteDataKeys.Permission]: permission };
}

/**
 * The background jobs screens, mounted under `/admin/jobs` by `ADMIN_ROUTES` (`loadChildren`): the job catalog and a
 * job's run history, both gated by `jobs.view`. Starting a run needs `jobs.run` (gated on the buttons and dialogs).
 */
export const JOBS_ROUTES: Routes = [
  {
    path: JobsRouteSegments.List,
    pathMatch: 'full',
    canActivate: [permissionGuard()],
    data: requires(JobsPermissionKeys.View),
    loadComponent: () => import('./jobs-page.component').then((m) => m.JobsPageComponent),
  },
  {
    path: JobsRouteSegments.Runs,
    canActivate: [permissionGuard()],
    data: requires(JobsPermissionKeys.View),
    loadComponent: () => import('./job-runs-page.component').then((m) => m.JobRunsPageComponent),
  },
];
