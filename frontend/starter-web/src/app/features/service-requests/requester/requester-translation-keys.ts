/** The texts of the Service Request requester screens, all under `serviceRequests.requester.*` (ar + en). */
export const ServiceRequestsRequesterTranslationKeys = {
  Nav: {
    Area: 'serviceRequests.requester.nav.area',
    Dashboard: 'serviceRequests.requester.nav.dashboard',
    Mine: 'serviceRequests.requester.nav.mine',
    New: 'serviceRequests.requester.nav.new',
  },
  Dashboard: {
    Greeting: 'serviceRequests.requester.dashboard.greeting',
    Kpis: 'serviceRequests.requester.dashboard.kpis',
    KpiAll: 'serviceRequests.requester.dashboard.kpi.all',
    KpiDraft: 'serviceRequests.requester.dashboard.kpi.draft',
    KpiSubmitted: 'serviceRequests.requester.dashboard.kpi.submitted',
    KpiUnderReview: 'serviceRequests.requester.dashboard.kpi.under-review',
    KpiReturned: 'serviceRequests.requester.dashboard.kpi.returned',
    KpiApproved: 'serviceRequests.requester.dashboard.kpi.approved',
    KpiRejected: 'serviceRequests.requester.dashboard.kpi.rejected',
    QuickActions: 'serviceRequests.requester.dashboard.quick-actions',
    ActionNew: 'serviceRequests.requester.dashboard.action-new',
    ActionMine: 'serviceRequests.requester.dashboard.action-mine',
    ActionNotifications: 'serviceRequests.requester.dashboard.action-notifications',
    Recent: 'serviceRequests.requester.dashboard.recent',
    RecentEmpty: 'serviceRequests.requester.dashboard.recent-empty',
    LoadFailed: 'serviceRequests.requester.dashboard.load-failed',
    Retry: 'serviceRequests.requester.dashboard.retry',
    ViewAll: 'serviceRequests.requester.dashboard.view-all',
  },
  Mine: {
    Title: 'serviceRequests.requester.mine.title',
    Empty: 'serviceRequests.requester.mine.empty',
    New: 'serviceRequests.requester.mine.new',
    StatusFilter: 'serviceRequests.requester.mine.status-filter',
    StatusAll: 'serviceRequests.requester.mine.status-all',
    NoNumber: 'serviceRequests.requester.mine.no-number',
    Open: 'serviceRequests.requester.mine.open',
    Continue: 'serviceRequests.requester.mine.continue',
    ColumnNumber: 'serviceRequests.requester.mine.columns.number',
    ColumnStatus: 'serviceRequests.requester.mine.columns.status',
    ColumnCreated: 'serviceRequests.requester.mine.columns.created',
    ColumnSubmitted: 'serviceRequests.requester.mine.columns.submitted',
  },
  Wizard: {
    TitleNew: 'serviceRequests.requester.wizard.title-new',
    TitleResume: 'serviceRequests.requester.wizard.title-resume',
    Loading: 'serviceRequests.requester.wizard.loading',
    LoadFailed: 'serviceRequests.requester.wizard.load-failed',
    Retry: 'serviceRequests.requester.wizard.retry',
    DraftSaved: 'serviceRequests.requester.wizard.draft-saved',
    Submit: 'serviceRequests.requester.wizard.submit',
    CategoryPlaceholder: 'serviceRequests.requester.wizard.category-placeholder',
    CategoriesFailed: 'serviceRequests.requester.wizard.categories-failed',
    PhoneHint: 'serviceRequests.requester.wizard.phone-hint',
    AttachmentsHint: 'serviceRequests.requester.wizard.attachments-hint',
    AttachmentsMissing: 'serviceRequests.requester.wizard.attachments-missing',
    Review: 'serviceRequests.requester.wizard.review',
    BackToMine: 'serviceRequests.requester.wizard.back-to-mine',
    SuccessTitle: 'serviceRequests.requester.wizard.success.title',
    SuccessMessage: 'serviceRequests.requester.wizard.success.message',
    SuccessView: 'serviceRequests.requester.wizard.success.view',
    SuccessMine: 'serviceRequests.requester.wizard.success.mine',
  },
  Detail: {
    Title: 'serviceRequests.requester.detail.title',
    Back: 'serviceRequests.requester.detail.back',
    ContinueDraft: 'serviceRequests.requester.detail.continue-draft',
    ReturnedTitle: 'serviceRequests.requester.detail.returned-title',
    RejectedTitle: 'serviceRequests.requester.detail.rejected-title',
    ActionsTitle: 'serviceRequests.requester.detail.actions-title',
  },
} as const;

/** The data-set label of the wizard's DevAutofill set (the `devAutofill` segment is required by the tool). */
export const SERVICE_REQUEST_DEV_AUTOFILL_LABEL_KEY = 'serviceRequests.devAutofill.valid';

function leaves(tree: object): string[] {
  return Object.values(tree).flatMap((value: unknown) =>
    typeof value === 'string' ? [value] : typeof value === 'object' && value !== null ? leaves(value) : []);
}

/** Every key the requester screens render, for the bundle check. */
export const SERVICE_REQUESTS_REQUESTER_TRANSLATION_KEYS: readonly string[] = [
  ...leaves(ServiceRequestsRequesterTranslationKeys),
  SERVICE_REQUEST_DEV_AUTOFILL_LABEL_KEY,
];
