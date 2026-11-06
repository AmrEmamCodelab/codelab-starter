/**
 * Every translation key the admin oversight screens render, under `serviceRequests.admin.*` in
 * `src/assets/i18n/ar.json` and `en.json` (the record card's keys are the staff screens' `serviceRequests.record.*`).
 */
export const ServiceRequestsAdminTranslationKeys = {
  /** The admin home tile (`admin-page/admin-tiles.ts`). */
  Tile: {
    Title: 'serviceRequests.admin.tile.title',
    Text: 'serviceRequests.admin.tile.text',
  },
  List: {
    Title: 'serviceRequests.admin.list.title',
    ReadOnly: 'serviceRequests.admin.list.read-only',
    Empty: 'serviceRequests.admin.list.empty',
    Search: 'serviceRequests.admin.list.search',
    StatusFilter: 'serviceRequests.admin.list.filters.status',
    AnyStatus: 'serviceRequests.admin.list.filters.any-status',
    ColumnNumber: 'serviceRequests.admin.list.columns.number',
    ColumnStatus: 'serviceRequests.admin.list.columns.status',
    ColumnOwner: 'serviceRequests.admin.list.columns.owner',
    ColumnSubmittedAt: 'serviceRequests.admin.list.columns.submitted-at',
    ColumnUpdatedAt: 'serviceRequests.admin.list.columns.updated-at',
    NoNumber: 'serviceRequests.admin.list.no-number',
    NotSubmitted: 'serviceRequests.admin.list.not-submitted',
  },
  Detail: {
    Title: 'serviceRequests.admin.detail.title',
    Back: 'serviceRequests.admin.detail.back',
    ReadOnly: 'serviceRequests.admin.detail.read-only',
    History: 'serviceRequests.admin.detail.history',
  },
} as const;

/** The flat list of every admin-screen key. */
export const SERVICE_REQUESTS_ADMIN_TRANSLATION_KEYS: readonly string[] = Object.values(ServiceRequestsAdminTranslationKeys).flatMap(
  (group) => Object.values(group),
);
