/**
 * The texts both frames (portal shell and requester shell) render around the page: the brand fallback, the digital
 * stamp strip, the skip link and the header's accessible names. Under `layouts.*` in both bundles.
 */
export const LayoutChromeTranslationKeys = {
  /** The product name when `config.json` carries no branding block. */
  BrandName: 'layouts.brand.name',
  StampText: 'layouts.stamp.text',
  StampVerify: 'layouts.stamp.verify',
  StampDomain: 'layouts.stamp.domain',
  StampSecure: 'layouts.stamp.secure',
  SkipLink: 'layouts.header.skip-link',
  NavLabel: 'layouts.header.nav-label',
  MenuLabel: 'layouts.header.menu-label',
  SignIn: 'layouts.header.sign-in',
} as const;

/** The portal navigation labels (see `PORTAL_NAV_ENTRIES`). */
export const PortalNavTranslationKeys = {
  Landing: 'layouts.portal-nav.landing',
  ServiceGuide: 'layouts.portal-nav.service-guide',
  Faq: 'layouts.portal-nav.faq',
  Contact: 'layouts.portal-nav.contact',
} as const;
