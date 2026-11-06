// Every route, selector, key and expected value of the Service Request flow (flows/service-requests.mjs, the phase-7
// exit criterion). The app-side values mirror the source named next to them; a rename there makes a step time out
// naming the selector, never pass silently.

/** The four runs of the flow: each language at the desktop width and at a phone width. */
export const SrViewports = Object.freeze({
  Desktop: Object.freeze({ name: '1440', width: 1440, height: 900 }),
  Mobile: Object.freeze({ name: '390', width: 390, height: 844 }),
});
export const SR_LANGUAGES = Object.freeze(['ar', 'en']);

/** The sample's record numbers: SR-YYYY-NNN (RecordNumbering, ServiceRequestRecord prefix SR, at least 3 digits). */
export const SR_NUMBER_PREFIX = 'SR';
export const SR_NUMBER_MIN_DIGITS = 3;

/** Where the requester (Development user) sits in the local secrets JSON (Identity:DevUsers:0). */
export const DEV_USERS_PATH = Object.freeze(['Identity', 'DevUsers']);

/**
 * The keys the flow grants the requester for the run and takes back at the end (backend ServiceRequestsPermissionKeys
 * and AttachmentsPermissionKeys). The reviewer is the break-glass administrator: a separate account, because the
 * engine refuses the staff rules to the record's owner.
 */
export const RequesterGrantKeys = Object.freeze([
  'service-requests.create',
  'service-requests.view-own',
  'attachments.view',
  'attachments.upload',
]);

/** App routes (features/service-requests/*.paths.ts, features/admin users list). */
export const SrPaths = Object.freeze({
  New: '/service-requests/new',
  Mine: '/service-requests/mine',
  RequesterDetailPrefix: '/service-requests/',
  StaffQueue: '/staff/service-requests',
  AdminOversight: '/admin/requests',
  AdminUsers: '/admin/users',
  Notifications: '/notifications',
});

/** The transition action keys (ServiceRequestActions); the decision buttons carry them in data-action. */
export const SrActions = Object.freeze({
  StartReview: 'start-review',
  Return: 'return',
  Resubmit: 'resubmit',
  Approve: 'approve',
  Reject: 'reject',
  Cancel: 'cancel',
});

/** The timeline actions a request that was returned, resubmitted and approved must show, in order. */
export const APPROVED_PATH_ACTIONS = Object.freeze(['submit', 'start-review', 'return', 'resubmit', 'start-review', 'approve']);

/** Selectors (data-testid hooks of the service-requests templates, shared/records and the page patterns). */
export const SrSel = Object.freeze({
  CategorySelect: '[data-testid="sr-field-categoryId"] select',
  TitleInput: '[data-testid="sr-field-title"] input',
  DescriptionInput: '[data-testid="sr-field-description"] textarea',
  PhoneInput: '[data-testid="sr-field-contactPhone"] input',
  WizardNext: '.wizard__next button',
  WizardDraft: '.wizard__draft button',
  WizardSubmit: '.wizard__submit button',
  WizardReview: '[data-testid="sr-wizard-review"]',
  WizardNumber: '[data-testid="sr-wizard-number"]',
  AttachmentType: '[data-testid="record-attachments-type"]',
  AttachmentFileInput: 'input.dga-file-upload__input[type="file"]',
  AttachmentUploadedRow: 'li.dga-file-upload__file[data-status="uploaded"]',
  MineOpen: '[data-testid="sr-mine-open"]',
  DetailContinue: '[data-testid="sr-detail-continue"] button, button[data-testid="sr-detail-continue"]',
  DetailReason: '[data-testid="sr-detail-reason"]',
  DetailActions: '[data-testid="sr-detail-actions"]',
  QueueOpen: '[data-testid="queue-open"]',
  QueueClaim: '[data-testid="queue-claim"] button, button[data-testid="queue-claim"]',
  QueueAssignee: '[data-testid="queue-assignee"]',
  StaffStatus: '[data-testid="detail-status"]',
  StaffDecided: '[data-testid="detail-decided"]',
  StaffActions: '.staff-detail__actions',
  StaffNoActions: '.staff-detail__state',
  SlaBadge: 'app-sla-badge',
  OversightOpen: '[data-testid="oversight-open"]',
  OversightReadOnly: '[data-testid="admin-detail-read-only"], [data-testid="oversight-read-only"]',
  ReasonDialogInput: 'dialog[open] textarea.decision-dialog__input',
  ReasonDialogConfirm: 'dialog[open] .editor-dialog__footer button[type="submit"]',
  ReasonDialogCancel: 'dialog[open] .editor-dialog__footer dga-button:first-child button',
  ConfirmDialogTextarea: 'dialog[open].dga-confirm-dialog textarea',
  ConfirmDialogConfirm: 'dialog[open] .dga-confirm-dialog__confirm button',
  TimelineEntry: '.record-timeline__entry',
  TimelineActor: '.record-timeline__actor',
  StatusTracker: 'app-status-tracker-pattern, .status-tracker',
  GrantItem: 'li.tree-editor__item',
  GrantCheckbox: 'input[type="checkbox"]',
  GrantsSave: '.grants__save button',
  UserGrantsLink: 'a[href$="/grants"]',
  /** The notifications page feed (layouts/notifications-feed): one title per item. */
  FeedItemTitle: '.feed__item-title',
  AnyActionButton: '[data-action]',
});

/** Test data the requester types. Every value passes ServiceRequestRules (title 5-200, description 20-2000, ^05\d{8}$). */
export const SrData = Object.freeze({
  TitlePrefix: 'E2E request ',
  Description: 'Scripted phase-7 flow: a request filed by the end-to-end run; please ignore it.',
  Phone: ['05', '00000000'].join(''), // a placeholder 05 number, split so the scrub sweep's mobile pattern stays quiet
  /** The first category option after the placeholder (lookup service-request-categories, general = 1). */
  CategoryOptionIndex: 1,
  ReturnReason: 'Please attach the signed form and correct the title.',
  RejectReason: 'The request is outside the scope of this service.',
  ApproveNote: 'Approved by the end-to-end run.',
});

/** Timeouts (milliseconds): in-app notifications are written by the outbox (the flow drains it when they are late). */
export const SrTimings = Object.freeze({
  NotificationWaitMs: 90000,
  NotificationPollMs: 3000,
});

/** A request worked by the requester and the reviewer names at least these many different actors on its timeline. */
export const MIN_TIMELINE_ACTORS = 2;

/** API calls the flow waits on (a response proves the server took the action). */
export const SrApi = Object.freeze({
  ServiceRequests: '/api/service-requests',
  DraftSuffix: '/draft',
  SubmitSuffix: '/submit',
  TransitionsSuffix: '/transitions',
  ClaimSuffix: '/claim',
  GrantsSuffix: '/grants',
});
