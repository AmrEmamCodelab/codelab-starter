import { PortalPaths } from '../../core/routing/route-paths';
import { LAYOUT_TIME_ZONE } from '../notifications-menu/notifications';
import { portalLink, type LayoutNavEntry } from '../portal-shell/portal-nav';
import { FooterTranslationKeys } from './footer-translation-keys';

/** The footer links, in display order: the public portal's privacy, terms and contact pages. */
export const FOOTER_LINKS: readonly LayoutNavEntry[] = [
  { id: 'privacy', labelKey: FooterTranslationKeys.Privacy, link: portalLink(PortalPaths.Privacy) },
  { id: 'terms', labelKey: FooterTranslationKeys.Terms, link: portalLink(PortalPaths.Terms) },
  { id: 'contact', labelKey: FooterTranslationKeys.Contact, link: portalLink(PortalPaths.Contact) },
];

/** A locale that prints the year as plain Latin digits. */
const YEAR_LOCALE = 'en-US';
const YEAR_FORMAT: Intl.DateTimeFormatOptions = { year: 'numeric', timeZone: LAYOUT_TIME_ZONE };

/** The year of the copyright line: the current Gregorian year at `epochMilliseconds`, in Saudi time. */
export function copyrightYear(epochMilliseconds: number): number {
  return Number(new Intl.DateTimeFormat(YEAR_LOCALE, YEAR_FORMAT).format(new Date(epochMilliseconds)));
}
