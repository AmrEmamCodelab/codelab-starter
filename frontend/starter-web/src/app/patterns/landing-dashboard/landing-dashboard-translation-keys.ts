/**
 * Every translation key the landing-dashboard pattern renders on its own, under `patterns.landing-dashboard.*` in
 * `src/assets/i18n/ar.json` and `en.json`. The greeting, tile labels and action labels are the screen's keys.
 */
export const LandingDashboardTranslationKeys = {
  /** Heading of the KPI tile row ("At a glance"). */
  Kpis: 'patterns.landing-dashboard.kpis',
  /** Heading of the quick actions. */
  QuickActions: 'patterns.landing-dashboard.quick-actions',
  /** Heading of the recent items. */
  Recent: 'patterns.landing-dashboard.recent',
  /** Shown in a tile while its value is loading or unknown. */
  ValueLoading: 'patterns.landing-dashboard.value-loading',
} as const;

/** The flat list of every landing-dashboard key. */
export const LANDING_DASHBOARD_TRANSLATION_KEYS: readonly string[] = Object.values(LandingDashboardTranslationKeys);
