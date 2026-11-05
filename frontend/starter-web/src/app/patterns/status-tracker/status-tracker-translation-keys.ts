/**
 * Every translation key the status tracker renders on its own (status names come from each entry's `statusKey`).
 * All live under `patterns.status-tracker.*` in `src/assets/i18n/ar.json` and `en.json`.
 */
export const StatusTrackerTranslationKeys = {
  /** Default heading of the tracker. */
  Title: 'patterns.status-tracker.title',
  CurrentStatus: 'patterns.status-tracker.current-status',
  /** `{{date}}`: when the current status was reached. */
  Since: 'patterns.status-tracker.since',
  History: 'patterns.status-tracker.history',
  /** `{{actor}}`: who made the change. */
  By: 'patterns.status-tracker.by',
  Reason: 'patterns.status-tracker.reason',
  Empty: 'patterns.status-tracker.empty',
} as const;

/** The flat list of every status tracker key. */
export const STATUS_TRACKER_TRANSLATION_KEYS: readonly string[] = Object.values(StatusTrackerTranslationKeys);
