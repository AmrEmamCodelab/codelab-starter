/**
 * Every translation key the staff-detail pattern renders on its own, under `patterns.staff-detail.*` in
 * `src/assets/i18n/ar.json` and `en.json`. The title, card headings and action labels are the screen's keys.
 */
export const StaffDetailTranslationKeys = {
  /** Heading of the decision panel. */
  Decision: 'patterns.staff-detail.decision',
  /** Shown in the decision panel when the server allows no action on the record. */
  NoActions: 'patterns.staff-detail.no-actions',
  /** Heading of the history section. */
  History: 'patterns.staff-detail.history',
  /** Accessible name of the detail-cards region. */
  Details: 'patterns.staff-detail.details',
  Loading: 'patterns.staff-detail.loading',
  Error: 'patterns.staff-detail.error',
  Retry: 'patterns.staff-detail.retry',
} as const;

/** The flat list of every staff-detail key. */
export const STAFF_DETAIL_TRANSLATION_KEYS: readonly string[] = Object.values(StaffDetailTranslationKeys);
