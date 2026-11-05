import type { DgaIconName, DgaNavItem } from '@codelab/ng-dga';
import { PortalPaths, RoutePaths } from '../../core/routing/route-paths';
import { PortalNavTranslationKeys } from './layout-chrome-translation-keys';

/** The separator between router path segments; every link here is absolute (starts with it). */
const SEGMENT_SEPARATOR = '/';

/** A page of the public portal (a child path under `RoutePaths.Portal`). */
export type PortalPath = (typeof PortalPaths)[keyof typeof PortalPaths];

/** The absolute router link of a top-level app page (`/login`); the empty path is the app root `/`. */
export function rootLink(path: string): string {
  return `${SEGMENT_SEPARATOR}${path}`;
}

/** The absolute router link of a portal page: `/portal` for the landing page, `/portal/<path>` for the others. */
export function portalLink(path: PortalPath): string {
  const base = rootLink(RoutePaths.Portal);
  return path === PortalPaths.Landing ? base : `${base}${SEGMENT_SEPARATOR}${path}`;
}

/** One entry of a layout's header navigation, before translation. */
export interface LayoutNavEntry {
  /** Stable key; unique among its siblings. */
  readonly id: string;
  /** Translation key of the visible label. */
  readonly labelKey: string;
  readonly link: string;
  readonly icon?: DgaIconName;
}

/** The public portal's header navigation, in display order. */
export const PORTAL_NAV_ENTRIES: readonly LayoutNavEntry[] = [
  { id: 'landing', labelKey: PortalNavTranslationKeys.Landing, link: portalLink(PortalPaths.Landing), icon: 'home' },
  {
    id: 'service-guide',
    labelKey: PortalNavTranslationKeys.ServiceGuide,
    link: portalLink(PortalPaths.ServiceGuide),
    icon: 'file',
  },
  { id: 'faq', labelKey: PortalNavTranslationKeys.Faq, link: portalLink(PortalPaths.Faq), icon: 'question' },
  { id: 'contact', labelKey: PortalNavTranslationKeys.Contact, link: portalLink(PortalPaths.Contact), icon: 'phone' },
];

/** The entries as `<dga-header>` items, each label translated. */
export function toDgaNavItems(
  entries: readonly LayoutNavEntry[],
  translate: (key: string) => string,
): readonly DgaNavItem[] {
  return entries.map(entry => ({ id: entry.id, label: translate(entry.labelKey), link: entry.link, icon: entry.icon }));
}
