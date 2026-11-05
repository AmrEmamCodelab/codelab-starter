import { describe, expect, it } from 'vitest';
import { PortalLinks, portalBreadcrumbs } from './portal-links';
import { PortalCommonTranslationKeys } from './portal-translation-keys';

describe('PortalLinks', () => {
  it('are absolute links under /portal', () => {
    expect(PortalLinks).toEqual({
      Landing: '/portal',
      ServiceGuide: '/portal/service-guide',
      Faq: '/portal/faq',
      Contact: '/portal/contact',
      Privacy: '/portal/privacy',
      Terms: '/portal/terms',
    });
  });
});

describe('portalBreadcrumbs', () => {
  it('leads from the portal home to the current page, translated', () => {
    const translate = (key: string): string => `t(${key})`;
    expect(portalBreadcrumbs(translate, 'portal.faq.title')).toEqual([
      { label: `t(${PortalCommonTranslationKeys.Home})`, link: PortalLinks.Landing, icon: 'home' },
      { label: 't(portal.faq.title)', current: true },
    ]);
  });
});
