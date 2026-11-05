import type { DgaBreadcrumbItem } from '@codelab/ng-dga';
import { PortalPaths } from '../../core/routing/route-paths';
import { portalLink } from '../../layouts/portal-shell/portal-nav';
import { PortalCommonTranslationKeys } from './portal-translation-keys';

/** Absolute router links of the portal pages (`/portal`, `/portal/faq`...), built from the route constants. */
export const PortalLinks = {
  Landing: portalLink(PortalPaths.Landing),
  ServiceGuide: portalLink(PortalPaths.ServiceGuide),
  Faq: portalLink(PortalPaths.Faq),
  Contact: portalLink(PortalPaths.Contact),
  Privacy: portalLink(PortalPaths.Privacy),
  Terms: portalLink(PortalPaths.Terms),
} as const;

/**
 * The breadcrumb trail of a portal page: the portal home, then the page itself (the current crumb).
 *
 * @param translate resolves a translation key in the active language.
 * @param pageTitleKey the translation key of the page title.
 */
export function portalBreadcrumbs(translate: (key: string) => string, pageTitleKey: string): DgaBreadcrumbItem[] {
  return [
    { label: translate(PortalCommonTranslationKeys.Home), link: PortalLinks.Landing, icon: 'home' },
    { label: translate(pageTitleKey), current: true },
  ];
}
