import type { Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import { LookupsPermissionKeys } from './lookups-permissions';
import { LookupsRouteSegments } from './lookups.paths';

/**
 * The lookups screens, spread into `ADMIN_ROUTES` (`...LOOKUPS_ROUTES`): the lookup types and the values of one
 * type, both gated by `lookups.view`. The activate / deactivate actions are gated by `lookups.manage` in the page.
 */
export const LOOKUPS_ROUTES: Routes = [
  {
    path: LookupsRouteSegments.Types,
    pathMatch: 'full',
    canActivate: [permissionGuard()],
    data: { [RouteDataKeys.Permission]: LookupsPermissionKeys.View },
    loadComponent: () => import('./lookup-types-page.component').then((m) => m.LookupTypesPageComponent),
  },
  {
    path: LookupsRouteSegments.Items,
    canActivate: [permissionGuard()],
    data: { [RouteDataKeys.Permission]: LookupsPermissionKeys.View },
    loadComponent: () => import('./lookup-items-page.component').then((m) => m.LookupItemsPageComponent),
  },
];
