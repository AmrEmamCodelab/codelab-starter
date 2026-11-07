import type { Routes } from '@angular/router';
import { AccessRefusal, RouteDataKeys, RoutePaths } from './core/routing/route-paths';
import { ShellComponent } from './layouts/shell/shell.component';

/**
 * The console's routes, relative to the base href. Every screen sits inside the shell; the access pages (401, 403) sit
 * outside it so nothing else of the console shows. Authorization is the BFF's: a screen the operator may not use
 * answers 403, and the interceptor moves to the access page. Unknown paths land on the overview.
 */
export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    children: [
      { path: '', pathMatch: 'full', redirectTo: RoutePaths.Overview },
      {
        path: RoutePaths.Overview,
        loadComponent: () => import('./features/overview/overview.page').then((m) => m.OverviewPage),
      },
      {
        path: RoutePaths.Reference,
        loadComponent: () => import('./features/reference/reference-lookup.page').then((m) => m.ReferenceLookupPage),
      },
      {
        path: RoutePaths.Jobs,
        loadComponent: () => import('./features/jobs/jobs.page').then((m) => m.JobsPage),
      },
      {
        path: RoutePaths.Certificates,
        loadComponent: () => import('./features/certificates/certificates.page').then((m) => m.CertificatesPage),
      },
    ],
  },
  {
    path: RoutePaths.SignInRequired,
    data: { [RouteDataKeys.Refusal]: AccessRefusal.Unauthenticated },
    loadComponent: () => import('./features/access/access.page').then((m) => m.AccessPage),
  },
  {
    path: RoutePaths.AccessDenied,
    data: { [RouteDataKeys.Refusal]: AccessRefusal.Forbidden },
    loadComponent: () => import('./features/access/access.page').then((m) => m.AccessPage),
  },
  { path: '**', redirectTo: RoutePaths.Overview },
];
