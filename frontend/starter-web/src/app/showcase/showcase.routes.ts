import type { Type } from '@angular/core';
import type { Route, Routes } from '@angular/router';
import { provideShowcaseClock } from './showcase-clock';
import { showcaseEnabledGuard } from './showcase-enabled.guard';
import { SHOWCASE_PAGES, ShowcasePageId, showcaseFullPath, type ShowcasePage } from './showcase-pages';

type PageLoader = () => Promise<Type<unknown>>;

/** The lazy page component of every showcase page (one bundle per page; none loads while the flag is off). */
export const SHOWCASE_PAGE_LOADERS: Readonly<Record<ShowcasePageId, PageLoader>> = {
  [ShowcasePageId.Components]: () => import('./components/components-page.component').then(m => m.ComponentsPageComponent),
  [ShowcasePageId.AdminList]: () => import('./patterns/admin-list-page.component').then(m => m.AdminListPageComponent),
  [ShowcasePageId.TreeEditor]: () => import('./patterns/tree-editor-page.component').then(m => m.TreeEditorPageComponent),
  [ShowcasePageId.WorkQueue]: () => import('./patterns/work-queue-page.component').then(m => m.WorkQueuePageComponent),
  [ShowcasePageId.StaffDetail]: () => import('./patterns/staff-detail-page.component').then(m => m.StaffDetailPageComponent),
  [ShowcasePageId.Wizard]: () => import('./patterns/wizard-page.component').then(m => m.WizardPageComponent),
  [ShowcasePageId.RequesterDetail]: () =>
    import('./patterns/requester-detail-page.component').then(m => m.RequesterDetailPageComponent),
  [ShowcasePageId.PortalContent]: () =>
    import('./patterns/portal-content-page.component').then(m => m.PortalContentPageComponent),
  [ShowcasePageId.LandingDashboard]: () =>
    import('./patterns/landing-dashboard-page.component').then(m => m.LandingDashboardPageComponent),
  [ShowcasePageId.Layouts]: () => import('./layouts/layouts-page.component').then(m => m.LayoutsPageComponent),
  [ShowcasePageId.PortalShell]: () => import('./layouts/portal-shell-page.component').then(m => m.PortalShellPageComponent),
  [ShowcasePageId.RequesterShell]: () =>
    import('./layouts/requester-shell-page.component').then(m => m.RequesterShellPageComponent),
  [ShowcasePageId.Branding]: () => import('./branding/branding-page.component').then(m => m.BrandingPageComponent),
};

function pageRoute(page: ShowcasePage): Route {
  return {
    path: showcaseFullPath(page.segment),
    canMatch: [showcaseEnabledGuard],
    providers: [provideShowcaseClock()],
    loadComponent: SHOWCASE_PAGE_LOADERS[page.id],
  };
}

/**
 * The showcase pages as TOP-LEVEL routes, each with its full path (`showcase`, `showcase/patterns/wizard`...), the
 * {@link showcaseEnabledGuard} and the pinned showcase clock. The app spreads them into its route table before the
 * staff shell; they are flat on purpose: the route gate exempts a public path only on the outermost route with that
 * path, so a pathless child page under a `showcase` mount would need a permission key.
 */
export const SHOWCASE_ROUTES: Routes = SHOWCASE_PAGES.map(pageRoute);

/** The full paths of the showcase pages, for `PUBLIC_ROUTE_PATHS`: every visitor may open them while the flag is on. */
export const SHOWCASE_PUBLIC_PATHS: readonly string[] = SHOWCASE_PAGES.map(page => showcaseFullPath(page.segment));
