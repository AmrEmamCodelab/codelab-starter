import type { Routes } from '@angular/router';
import { PortalPaths, RoutePaths } from '../../core/routing/route-paths';
import { PRIVACY_DOCUMENT, TERMS_DOCUMENT } from './legal/legal-content';

/** Route `data` keys the portal pages read (bound to the page's inputs by `withComponentInputBinding`). */
export const PortalRouteDataKeys = {
  /** The {@link LegalDocument} the legal page renders (privacy policy or terms of use). */
  Document: 'document',
} as const;

const PATH_SEPARATOR = '/';

/** The full route path (no leading slash) of a portal page: `portal` for the landing page, `portal/<path>` otherwise. */
export function portalRoutePath(path: string): string {
  return path === PortalPaths.Landing ? RoutePaths.Portal : `${RoutePaths.Portal}${PATH_SEPARATOR}${path}`;
}

/**
 * The public portal pages, mounted by the app under `RoutePaths.Portal` inside the portal shell
 * (`{ path: RoutePaths.Portal, component: PortalShellComponent, children: PORTAL_ROUTES }`). Every page is public:
 * no guard and no permission key; their full paths are {@link PORTAL_PUBLIC_PATHS}.
 */
export const PORTAL_ROUTES: Routes = [
  {
    path: PortalPaths.Landing,
    pathMatch: 'full',
    loadComponent: () => import('./landing/portal-landing-page.component').then(m => m.PortalLandingPageComponent),
  },
  {
    path: PortalPaths.ServiceGuide,
    loadComponent: () =>
      import('./service-guide/service-guide-page.component').then(m => m.ServiceGuidePageComponent),
  },
  {
    path: PortalPaths.Faq,
    loadComponent: () => import('./faq/faq-page.component').then(m => m.FaqPageComponent),
  },
  {
    path: PortalPaths.Contact,
    loadComponent: () => import('./contact/contact-page.component').then(m => m.ContactPageComponent),
  },
  {
    path: PortalPaths.Privacy,
    data: { [PortalRouteDataKeys.Document]: PRIVACY_DOCUMENT },
    loadComponent: () => import('./legal/legal-page.component').then(m => m.LegalPageComponent),
  },
  {
    path: PortalPaths.Terms,
    data: { [PortalRouteDataKeys.Document]: TERMS_DOCUMENT },
    loadComponent: () => import('./legal/legal-page.component').then(m => m.LegalPageComponent),
  },
];

/**
 * The full paths of the portal pages, for the app's `PUBLIC_ROUTE_PATHS` (the route-table gate): every page renders
 * without a session or a permission key.
 */
export const PORTAL_PUBLIC_PATHS: readonly string[] = Object.values(PortalPaths).map(portalRoutePath);
