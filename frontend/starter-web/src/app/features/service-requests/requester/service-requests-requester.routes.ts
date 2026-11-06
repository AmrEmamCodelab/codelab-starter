import type { Route, Routes } from '@angular/router';
import { permissionGuard } from '../../../core/auth/permission.guard';
import { RouteDataKeys } from '../../../core/routing/route-paths';
import type { RequesterNavItem } from '../../../layouts/requester-shell/requester-nav';
import { SERVICE_REQUESTS_PERMISSION_KEYS, type ServiceRequestsPermissionKey } from '../data';
import {
  SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK,
  SERVICE_REQUESTS_REQUESTER_PATH,
  SERVICE_REQUEST_ID_PARAM,
  ServiceRequestsRequesterPaths,
  serviceRequestsRequesterLinks,
} from './requester-paths';
import { ServiceRequestsRequesterTranslationKeys as Keys } from './requester-translation-keys';

/** Route data naming the permission `permissionGuard()` requires. */
function requires(permission: ServiceRequestsPermissionKey): Readonly<Record<string, string>> {
  return { [RouteDataKeys.Permission]: permission };
}

/** A page of the area: lazy, guarded by `permission`. */
function page(path: string, permission: ServiceRequestsPermissionKey, loadComponent: Route['loadComponent']): Route {
  return { path, pathMatch: 'full', canActivate: [permissionGuard()], data: requires(permission), loadComponent };
}

/**
 * The requester screens of the Service Request sample, mounted by the app as children of the requester shell (behind
 * `authGuard`). Every page carries its own key: the dashboard, my requests and the detail need
 * `service-requests.view-own`; the wizard (new request and draft resume) needs `service-requests.create`. Static
 * paths come before `:id`, so `mine` and `new` are never read as a request id.
 */
export const SERVICE_REQUESTS_REQUESTER_ROUTES: Routes = [
  {
    path: SERVICE_REQUESTS_REQUESTER_PATH,
    children: [
      page(ServiceRequestsRequesterPaths.Dashboard, SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn, () =>
        import('./dashboard/requester-dashboard.component').then((m) => m.RequesterDashboardComponent)),
      page(ServiceRequestsRequesterPaths.Mine, SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn, () =>
        import('./my-requests/my-requests.component').then((m) => m.MyRequestsComponent)),
      page(ServiceRequestsRequesterPaths.New, SERVICE_REQUESTS_PERMISSION_KEYS.Create, () =>
        import('./wizard/service-request-wizard.component').then((m) => m.ServiceRequestWizardComponent)),
      page(`${ServiceRequestsRequesterPaths.Drafts}/:${SERVICE_REQUEST_ID_PARAM}`, SERVICE_REQUESTS_PERMISSION_KEYS.Create, () =>
        import('./wizard/service-request-wizard.component').then((m) => m.ServiceRequestWizardComponent)),
      page(ServiceRequestsRequesterPaths.Detail, SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn, () =>
        import('./detail/service-request-detail.component').then((m) => m.ServiceRequestDetailComponent)),
    ],
  },
];

const DEFAULT_LINKS = serviceRequestsRequesterLinks(SERVICE_REQUESTS_REQUESTER_DEFAULT_BASE_LINK);

/** The stable ids of the navigation entries. */
export const ServiceRequestsRequesterNavIds = {
  Area: 'service-requests',
  Dashboard: 'service-requests-dashboard',
  Mine: 'service-requests-mine',
  New: 'service-requests-new',
} as const;

/** One leaf entry, gated by the key of the page it opens. */
function navEntry(id: string, labelKey: string, link: string, key: ServiceRequestsPermissionKey): RequesterNavItem {
  return { id, labelKey, link, permission: key };
}

/**
 * The requester shell's navigation entries of the sample (`RequesterShellOptions.navItems`), for a shell mounted at
 * the app root. Each entry's key is the key of the page it opens.
 */
export const SERVICE_REQUESTS_REQUESTER_NAV: readonly RequesterNavItem[] = Object.freeze([
  {
    id: ServiceRequestsRequesterNavIds.Area,
    labelKey: Keys.Nav.Area,
    icon: 'file',
    children: [
      navEntry(ServiceRequestsRequesterNavIds.Dashboard, Keys.Nav.Dashboard, DEFAULT_LINKS.dashboard, SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn),
      navEntry(ServiceRequestsRequesterNavIds.Mine, Keys.Nav.Mine, DEFAULT_LINKS.mine, SERVICE_REQUESTS_PERMISSION_KEYS.ViewOwn),
      navEntry(ServiceRequestsRequesterNavIds.New, Keys.Nav.New, DEFAULT_LINKS.create, SERVICE_REQUESTS_PERMISSION_KEYS.Create),
    ],
  },
]);
