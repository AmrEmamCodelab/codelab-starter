/**
 * Every translation key the portal-content pattern renders on its own, under `patterns.portal-content.*` in
 * `src/assets/i18n/ar.json` and `en.json`. The page title and the section titles are the screen's keys.
 */
export const PortalContentTranslationKeys = {
  /** Heading and accessible name of the table of contents ("On this page"). */
  Toc: 'patterns.portal-content.toc',
} as const;

/** The flat list of every portal-content key. */
export const PORTAL_CONTENT_TRANSLATION_KEYS: readonly string[] = Object.values(PortalContentTranslationKeys);
