import { TestBed } from '@angular/core/testing';
import type { Route, UrlSegment } from '@angular/router';
import { assertRoutesGated } from '@codelab/ng-tools/routes';
import { describe, expect, it } from 'vitest';
import { provideTestAppConfig } from '../core/config/app-config.testing';
import { RouteDataKeys, RoutePaths } from '../core/routing/route-paths';
import { APP_CLOCK } from '../core/session/clock';
import { SHOWCASE_NOW_MS } from './showcase-clock';
import { showcaseEnabledGuard } from './showcase-enabled.guard';
import { SHOWCASE_PAGES, ShowcasePageId, showcaseFullPath, showcaseLink, showcasePage, showcasePagesIn } from './showcase-pages';
import { SHOWCASE_PAGE_LOADERS, SHOWCASE_PUBLIC_PATHS, SHOWCASE_ROUTES } from './showcase.routes';
import { SHOWCASE_TEST_CONFIG } from './showcase.testing';

function guardAllows(showcase: boolean | undefined): unknown {
  TestBed.configureTestingModule({
    providers: [provideTestAppConfig({ ...SHOWCASE_TEST_CONFIG, showcase: showcase as boolean })],
  });
  return TestBed.runInInjectionContext(() => showcaseEnabledGuard({} as Route, [] as UrlSegment[]));
}

describe('showcaseEnabledGuard', () => {
  it('matches when config.json turns the showcase on', () => {
    expect(guardAllows(true)).toBe(true);
  });

  it.each([false, undefined])('does not match when the flag is %s, so the URL falls through to the 404 page', flag => {
    expect(guardAllows(flag)).toBe(false);
  });
});

describe('SHOWCASE_ROUTES', () => {
  it('has one flat route per page, at its full path under /showcase', () => {
    expect(SHOWCASE_ROUTES.map(route => route.path)).toEqual(SHOWCASE_PAGES.map(page => showcaseFullPath(page.segment)));
    for (const route of SHOWCASE_ROUTES) {
      expect(route.path === RoutePaths.Showcase || route.path?.startsWith(`${RoutePaths.Showcase}/`)).toBe(true);
      expect(route.children).toBeUndefined();
    }
  });

  it('guards every page with the showcase flag and pins the clock', () => {
    for (const route of SHOWCASE_ROUTES) {
      expect(route.canMatch).toEqual([showcaseEnabledGuard]);
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: route.providers ?? [] });
      expect(TestBed.inject(APP_CLOCK)()).toBe(SHOWCASE_NOW_MS);
    }
  });

  it('lazy-loads a standalone component for every page', async () => {
    for (const page of SHOWCASE_PAGES) {
      const component = await SHOWCASE_PAGE_LOADERS[page.id]();
      expect(typeof component, page.id).toBe('function');
    }
  });

  it('exports every page path as public, and passes the route-table gate with them', () => {
    expect(SHOWCASE_PUBLIC_PATHS).toEqual(SHOWCASE_ROUTES.map(route => route.path));
    expect(SHOWCASE_PUBLIC_PATHS).not.toContain('');
    expect(() =>
      assertRoutesGated(SHOWCASE_ROUTES, { permissionDataKey: RouteDataKeys.Permission, publicPaths: SHOWCASE_PUBLIC_PATHS }),
    ).not.toThrow();
  });

  it('fails the gate without the public paths: a showcase page is never silently public', () => {
    expect(() => assertRoutesGated(SHOWCASE_ROUTES, { permissionDataKey: RouteDataKeys.Permission })).toThrow();
  });
});

describe('the showcase page registry', () => {
  it('has unique ids and segments, and the gallery at /showcase itself (the catalog links there)', () => {
    expect(new Set(SHOWCASE_PAGES.map(page => page.id)).size).toBe(SHOWCASE_PAGES.length);
    expect(new Set(SHOWCASE_PAGES.map(page => page.segment)).size).toBe(SHOWCASE_PAGES.length);
    expect(showcaseLink(ShowcasePageId.Components)).toBe(`/${RoutePaths.Showcase}`);
    expect(showcaseLink(ShowcasePageId.Wizard)).toBe(`/${RoutePaths.Showcase}/patterns/wizard`);
  });

  it('shows all 8 page patterns (admin list, tree editor, work queue, staff detail, wizard, requester detail, portal content, landing dashboard)', () => {
    expect(showcasePagesIn('patterns').map(page => page.id)).toEqual([
      ShowcasePageId.AdminList,
      ShowcasePageId.TreeEditor,
      ShowcasePageId.WorkQueue,
      ShowcasePageId.StaffDetail,
      ShowcasePageId.Wizard,
      ShowcasePageId.RequesterDetail,
      ShowcasePageId.PortalContent,
      ShowcasePageId.LandingDashboard,
    ]);
  });

  it('refuses an unknown page id', () => {
    expect(() => showcasePage('missing' as ShowcasePageId)).toThrow('missing');
  });
});
