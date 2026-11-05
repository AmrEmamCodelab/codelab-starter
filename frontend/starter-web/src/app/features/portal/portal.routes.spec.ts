import type { Route } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { PortalPaths, RoutePaths } from '../../core/routing/route-paths';
import { ContactPageComponent } from './contact/contact-page.component';
import { FaqPageComponent } from './faq/faq-page.component';
import { PortalLandingPageComponent } from './landing/portal-landing-page.component';
import { PRIVACY_DOCUMENT, TERMS_DOCUMENT } from './legal/legal-content';
import { LegalPageComponent } from './legal/legal-page.component';
import { PORTAL_PUBLIC_PATHS, PORTAL_ROUTES, PortalRouteDataKeys, portalRoutePath } from './portal.routes';
import { ServiceGuidePageComponent } from './service-guide/service-guide-page.component';

function routeAt(path: string): Route {
  const route = PORTAL_ROUTES.find(candidate => candidate.path === path);
  if (!route) {
    throw new Error(`no portal route at '${path}'`);
  }
  return route;
}

describe('PORTAL_ROUTES', () => {
  it('has one route per portal path, in the PortalPaths order', () => {
    expect(PORTAL_ROUTES.map(route => route.path)).toEqual(Object.values(PortalPaths));
  });

  it('matches the landing page on the full empty path only', () => {
    expect(routeAt(PortalPaths.Landing).pathMatch).toBe('full');
  });

  it.each([
    [PortalPaths.Landing, PortalLandingPageComponent],
    [PortalPaths.ServiceGuide, ServiceGuidePageComponent],
    [PortalPaths.Faq, FaqPageComponent],
    [PortalPaths.Contact, ContactPageComponent],
    [PortalPaths.Privacy, LegalPageComponent],
    [PortalPaths.Terms, LegalPageComponent],
  ])('lazy-loads the page of %s', async (path, page) => {
    const route = routeAt(path);
    expect(route.component).toBeUndefined();
    await expect(route.loadComponent?.()).resolves.toBe(page);
  });

  it('passes the legal document through route data', () => {
    expect(routeAt(PortalPaths.Privacy).data).toEqual({ [PortalRouteDataKeys.Document]: PRIVACY_DOCUMENT });
    expect(routeAt(PortalPaths.Terms).data).toEqual({ [PortalRouteDataKeys.Document]: TERMS_DOCUMENT });
  });

  it('is public: no guard and no permission key on any page', () => {
    for (const route of PORTAL_ROUTES) {
      expect(route.canActivate).toBeUndefined();
      expect(route.canMatch).toBeUndefined();
      expect(Object.keys(route.data ?? {})).not.toContain('permission');
    }
  });
});

describe('PORTAL_PUBLIC_PATHS', () => {
  it('lists the full path of every portal page under the portal mount', () => {
    expect(PORTAL_PUBLIC_PATHS).toEqual([
      'portal',
      'portal/service-guide',
      'portal/faq',
      'portal/contact',
      'portal/privacy',
      'portal/terms',
    ]);
    expect(PORTAL_PUBLIC_PATHS).not.toContain('');
  });

  it('builds the paths from the route constants', () => {
    expect(portalRoutePath(PortalPaths.Landing)).toBe(RoutePaths.Portal);
    expect(portalRoutePath(PortalPaths.Faq)).toBe(`${RoutePaths.Portal}/${PortalPaths.Faq}`);
  });
});
