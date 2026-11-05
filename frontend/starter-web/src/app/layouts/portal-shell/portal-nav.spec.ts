import { describe, expect, it } from 'vitest';
import { PortalPaths, RoutePaths } from '../../core/routing/route-paths';
import { echoTranslate } from '../layout.testing';
import { PortalNavTranslationKeys } from './layout-chrome-translation-keys';
import { PORTAL_NAV_ENTRIES, portalLink, rootLink, toDgaNavItems } from './portal-nav';

describe('rootLink', () => {
  it('makes a top-level path absolute', () => {
    expect(rootLink(RoutePaths.Login)).toBe('/login');
    expect(rootLink(RoutePaths.Dashboard)).toBe('/');
  });
});

describe('portalLink', () => {
  it('links the landing page to the portal root', () => {
    expect(portalLink(PortalPaths.Landing)).toBe('/portal');
  });

  it('links every other page below the portal root', () => {
    expect(portalLink(PortalPaths.Faq)).toBe('/portal/faq');
    expect(portalLink(PortalPaths.Privacy)).toBe('/portal/privacy');
  });
});

describe('PORTAL_NAV_ENTRIES', () => {
  it('offers landing, service guide, FAQ and contact, in that order', () => {
    expect(PORTAL_NAV_ENTRIES.map(entry => entry.link)).toEqual([
      '/portal',
      '/portal/service-guide',
      '/portal/faq',
      '/portal/contact',
    ]);
    expect(PORTAL_NAV_ENTRIES.map(entry => entry.labelKey)).toEqual(Object.values(PortalNavTranslationKeys));
  });

  it('has unique ids', () => {
    const ids = PORTAL_NAV_ENTRIES.map(entry => entry.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});

describe('toDgaNavItems', () => {
  it('translates each label and keeps id, link and icon', () => {
    const [first] = toDgaNavItems(PORTAL_NAV_ENTRIES, echoTranslate);
    expect(first).toEqual({ id: 'landing', label: PortalNavTranslationKeys.Landing, link: '/portal', icon: 'home' });
  });
});
