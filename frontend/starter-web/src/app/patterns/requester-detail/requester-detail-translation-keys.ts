/**
 * Every translation key the requester detail pattern renders on its own (the page title, status names and section
 * contents come from the screen). All live under `patterns.requester-detail.*` in `src/assets/i18n/ar.json` and
 * `en.json`.
 */
export const RequesterDetailTranslationKeys = {
  RecordNumber: 'patterns.requester-detail.record-number',
  CreatedOn: 'patterns.requester-detail.created-on',
  Status: 'patterns.requester-detail.status',
  Loading: 'patterns.requester-detail.loading',
  Error: 'patterns.requester-detail.error',
  Retry: 'patterns.requester-detail.retry',
  /** Accessible name of the summary cards region. */
  Summary: 'patterns.requester-detail.summary',
  Attachments: 'patterns.requester-detail.attachments',
  Actions: 'patterns.requester-detail.actions',
} as const;

/** The flat list of every requester detail key. */
export const REQUESTER_DETAIL_TRANSLATION_KEYS: readonly string[] = Object.values(RequesterDetailTranslationKeys);
