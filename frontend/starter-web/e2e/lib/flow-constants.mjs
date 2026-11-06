// Every name, selector, key, limit and expected text of the scripted user flows (e2e/flows.mjs). Nothing else in the
// flow files writes a bare selector, timeout or app key. The app-side values mirror the source named next to them; a
// rename there breaks the flow loudly (a step times out naming the selector), never silently.

/** Exit codes of e2e/flows.mjs (same meaning as the visual harness). */
export const FlowExitCodes = Object.freeze({
  /** Every selected flow passed. */
  Ok: 0,
  /** A step of at least one flow failed. */
  Failed: 1,
  /** Bad arguments. */
  Usage: 2,
  /** A runtime package missing, Edge not starting, the app unreachable or no admin password. */
  Environment: 3,
});

/** The phase-5 and phase-6 exit-criteria flows, by command-line name. */
export const FlowNames = Object.freeze({
  /** Admin edits a runtime setting, runs a job and sees its run history. */
  SettingsJobs: 'settings-jobs',
  /** A test notification shows in the bell in ar and en, and its e-mail is dropped. */
  NotificationBell: 'notification-bell',
  /** An Excel-imported user gets the activation e-mail, activates, signs in; the reused link is refused. */
  Activation: 'activation',
  /** Contact-us returns a CU-YYYY-NNN number and notifies; an admin FAQ edit shows on the portal in ar and en. */
  ContactFaq: 'contact-faq',
  /** A PDF is uploaded on the demo record, listed, downloaded; an oversized file is refused in ar and en with a reference code. */
  Attachments: 'attachments',
});

export const ALL_FLOWS = Object.freeze(Object.values(FlowNames));
/** --flows value that selects every flow. */
export const ALL_FLOWS_TOKEN = 'all';

/** Step outcomes in the step log. */
export const StepStatus = Object.freeze({
  Passed: 'passed',
  Failed: 'failed',
  /** Not run because an earlier step of the same flow failed. */
  Skipped: 'skipped',
});

export const FLOW_REPORT_JSON = 'flows-report.json';
export const FLOW_REPORT_MARKDOWN = 'flows-report.md';
export const FLOW_SCREENSHOT_DIR = 'screenshots';
export const DEFAULT_FLOW_OUTPUT_FOLDER = 'codelab-flows';

/** The local secrets file run-local.ps1 and New-LocalSecrets.ps1 use (tools/run-local/lib/constants.mjs). */
export const DEFAULT_SECRETS_FILE = '../../backend/src/Starter.Api/appsettings.Development.local.json';
/** Environment variables that override the break-glass credentials from the secrets file. */
export const CredentialEnv = Object.freeze({ User: 'STARTER_E2E_USER', PasswordVar: 'STARTER_E2E_PASSWORD' });
/** Where the break-glass user sits in the secrets JSON (Identity:BreakGlass). */
export const BREAK_GLASS_PATH = Object.freeze(['Identity', 'BreakGlass']);

/** EmailDeliveryOptions.DefaultPickupFolderName: the FileDrop folder under the OS temp folder. */
export const DEFAULT_MAIL_FOLDER = 'starter-mail';
export const MAIL_EXTENSION = '.eml';

/** Timeouts and polling (milliseconds). */
export const FlowTimings = Object.freeze({
  StepTimeoutMs: 30000,
  /** Mail written by the outbox: the flow runs the drain job, then polls the pickup folder this long. */
  MailWaitMs: 60000,
  MailPollMs: 1000,
  /** A job run reaches a final state within this time (the runs screen reloads every 2 s). */
  JobRunWaitMs: 60000,
  /** Portal content edited by the admin reaches the public page within this time (reload polling). */
  ContentWaitMs: 45000,
  ContentPollMs: 2000,
});

/** App routes (core/routing/route-paths.ts, the admin and feature *.paths.ts files). */
export const AppPaths = Object.freeze({
  Login: '/login',
  Activate: '/activate',
  AdminSettings: '/admin/settings',
  AdminJobs: '/admin/jobs',
  AdminImport: '/admin/import',
  AdminPortalContent: '/admin/portal-content',
  Notifications: '/notifications',
  PortalContact: '/portal/contact',
  PortalFaq: '/portal/faq',
  /** The attachments demo page (features/attachments/attachments.routes.ts, development builds only). */
  AttachmentsDemo: '/attachments/demo',
  /** Query of the login page after a successful activation (RouteQueryParams.Notice = LoginNotices.Activated). */
  ActivatedNotice: 'notice=activated',
});

/** API paths the flows wait on (the generated OpenAPI client; a response proves the server took the action). */
export const ApiPaths = Object.freeze({
  UserImportPreview: '/api/admin/user-import/preview',
  UserImportCommitSuffix: '/commit',
  UserImportPrefix: '/api/admin/user-import/',
  Activate: '/api/auth/activate',
  ContactMessages: '/api/portal/contact-messages',
  AdminPortalContentPrefix: '/api/admin/portal/content/',
  AdminSettingsPrefix: '/api/admin/settings',
  NotificationsTest: '/api/admin/notifications/test',
  /** shared/attachments/attachments-api.ts: POST .../attachments links a receipt; DELETE /api/attachments/{id} removes. */
  RecordAttachmentsPrefix: '/api/records/',
  AttachmentsSuffix: '/attachments',
  AttachmentPrefix: '/api/attachments/',
});
export const HttpMethods = Object.freeze({ Post: 'POST', Put: 'PUT', Delete: 'DELETE' });
/** The largest HTTP status that is a success. */
export const MAX_SUCCESS_STATUS = 299;
/** The MIME type of an .xlsx upload. */
export const XLSX_MIME = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet';
export const IMPORT_FILE_NAME = 'e2e-users.xlsx';

/** Backend keys the flows act on (SettingKeys, JobKeys, ImportColumns, the import value words). */
export const AppKeys = Object.freeze({
  /** A whole-number setting that only changes the feed page size: safe to edit and reset. */
  Setting: 'notifications.page-size',
  /** The outbox drain job: running it delivers queued e-mail at once instead of after the 5-minute schedule. */
  OutboxDrainJob: 'outbox-drain',
  ImportKindInternal: 'internal',
  ImportBindingPassword: 'password',
});

/** The user import template headers, in template order (ImportColumns.Template). */
export const IMPORT_HEADERS = Object.freeze(['UserName', 'DisplayName', 'Email', 'Kind', 'Binding']);

/** Selectors. data-testid hooks come from the component templates; form fields use the inner element's name. */
export const Sel = Object.freeze({
  UserNameInput: 'input[name="userName"]',
  PasswordInput: 'input[name="password"]',
  ConfirmPasswordInput: 'input[name="confirmPassword"]',
  SubmitButton: 'form button[type="submit"]',
  LoginError: '.login__error',
  ActivateError: '.activate__error',
  OpenDialogConfirm: 'dialog[open] .editor-dialog__footer button[type="submit"]',
  ListRow: 'tr.admin-list__row',
  SettingEdit: '[data-testid="setting-edit"] button',
  SettingValue: '[data-testid="setting-value"]',
  EditorValueInput: '[data-testid="editor-value"] input',
  EditorReset: '[data-testid="editor-reset"] button',
  JobRun: '[data-testid="job-run"] button',
  RunStatus: '[data-testid="run-status"]',
  RunsJob: '[data-testid="runs-job"]',
  /** The pill inside dga-status-tag that carries the tone (ng-dga status-tag template). */
  StatusTone: '.dga-status[data-tone]',
  ImportFile: '[data-testid="import-file"] input[type="file"]',
  ImportPreview: '[data-testid="import-preview"] button',
  ImportCommit: '[data-testid="import-commit"] button',
  ImportSummary: '[data-testid="import-summary"]',
  /** The notifications page heading: rendered once the lazy route is in, before the send-test control is looked up. */
  NotificationsTitle: '#notifications-page-title',
  Bell: 'button.notifications__bell',
  BellItemTitle: '.notifications__panel .notifications__item-title',
  /** The notifications page control that sends the caller the test notification (POST api/admin/notifications/test). */
  SendTestNotification: '[data-testid="notifications-send-test"] button, button[data-testid="notifications-send-test"]',
  ContactFullName: 'input[name="fullName"]',
  ContactEmail: 'input[name="email"]',
  ContactTopic: 'select[name="topicId"]',
  ContactMessage: 'textarea[name="message"]',
  ContactSubmit: '[data-testid="contact-submit"] button',
  ContactFeedback: '[data-testid="contact-feedback"]',
  ContentTabs: '[data-testid="content-page-tabs"]',
  ContentSectionOpen: '[data-testid="content-section-open"] button',
  ContentTitleAr: '[data-testid="section-title-ar"] input',
  ContentTitleEn: '[data-testid="section-title-en"] input',
  ContentSave: '[data-testid="content-save"] button',
  ContentSaved: '[data-testid="content-saved"]',
  PortalMain: 'main',
  /** app-record-attachments (shared/record-attachments/record-attachments-keys.ts RecordAttachmentsTestIds.Type). */
  AttachmentType: '[data-testid="record-attachments-type"]',
  /** Inside one type section: the ng-dga file-upload control (dga-file-upload.component.html). */
  AttachmentFileInput: 'input.dga-file-upload__input[type="file"]',
  AttachmentRow: 'li.dga-file-upload__file',
  AttachmentRowError: '.dga-file-upload__file-error',
  AttachmentRowReference: '.dga-file-upload__reference-code',
  AttachmentDownload: '.dga-file-upload__download button',
  AttachmentRemove: '.dga-file-upload__remove button',
  /** The widget's page-feedback banner message (dga-page-feedback.component.html). */
  FeedbackMessage: '.dga-page-feedback__message',
});

/** Status-tag tones of a finished job run (features/admin/jobs/job-labels.ts: Succeeded success, Failed error). */
export const FINAL_RUN_TONES = Object.freeze(['success', 'error']);
/** The tone of a succeeded run. */
export const SUCCEEDED_RUN_TONE = 'success';

/** Texts the backend templates produce (TemplateSeed: test-notification, in-app channel). */
export const TestNotificationTitles = Object.freeze({ ar: 'إشعار تجريبي', en: 'Test notification' });
/** The jobs screens' label of the SYSTEM actor (assets/i18n admin.jobs.runs.actor-system). */
export const SystemActorLabels = Object.freeze({ ar: 'النظام', en: 'System' });
/** The portal content tab of the FAQ page (admin.portal-content.pages.*). */
export const FaqTabLabels = Object.freeze({ ar: 'الأسئلة الشائعة', en: 'FAQ' });

/** Test data the flows type. Unique per run through the run stamp; e-mail on the reserved .test domain. */
export const FlowData = Object.freeze({
  UserPrefix: 'e2e-',
  EmailDomain: 'example.test',
  DisplayNamePrefix: 'E2E user ',
  ContactName: 'E2E Contact',
  ContactMessage: 'Scripted phase-5 flow: please ignore this message.',
  FaqMarkerPrefix: 'E2E FAQ ',
  /** Tells the two language titles apart on the page. */
  FaqMarkerSuffixes: Object.freeze({ ar: 'عربي', en: 'English' }),
  /** Meets the activation password policy (length, classes) without being a real secret. */
  NewPasswordPrefix: 'E2e!Flow#',
  SettingDelta: 1,
});

/** dga-file-upload's row status attribute (DGA_FILE_UPLOAD_STATUS_NAMES). */
export const AttachmentRowStatus = Object.freeze({ Attribute: 'data-status', Uploaded: 'uploaded', Failed: 'failed' });

/**
 * The attachment service's Codelab:Security:Uploads:MaxFileBytes (services/attachment-service/.../appsettings.json,
 * 10 MiB). The oversized file is one byte more. A fork that lowers the limit still sees a refusal; one that raises it
 * must raise this too, or the oversized step fails by being accepted.
 */
export const ATTACHMENT_MAX_FILE_BYTES = 10 * 1024 * 1024;
/** The refusal code of a file over MaxFileBytes (shared/attachments/attachment-error-codes.ts FileTooLarge). */
export const FILE_TOO_LARGE_CODE = 'ATTACHMENTS_FILE_TOO_LARGE';
/**
 * errors.ATTACHMENTS_FILE_TOO_LARGE in src/assets/i18n/{ar,en}.json: the row's error text of the refused file
 * (tests/attachment-checks.test.mjs fails when a bundle changes and this copy does not).
 */
export const FileTooLargeTexts = Object.freeze({ ar: 'حجم الملف أكبر من الحجم المسموح به.', en: 'The file is larger than the allowed size.' });
/** The PDF MIME type and the bytes the service's signature check reads (header first). */
export const PDF_MIME = 'application/pdf';
export const PDF_HEADER = '%PDF-1.7\n';
export const PDF_TRAILER = '%%EOF\n';
export const PDF_EXTENSION = '.pdf';
/** The padding byte of the oversized file (a space: still a well-formed PDF comment area for the header check). */
export const PDF_PADDING_BYTE = 0x20;
/** Names of the flow's files: e2e-<token>.pdf (kept, then removed) and e2e-<token>-oversized-<lang>.pdf (refused). */
export const AttachmentFileNames = Object.freeze({ Prefix: 'e2e-', OversizedInfix: '-oversized-' });
/** The ticket header the browser sends to the attachment service (attachments-api.ts UPLOAD_TICKET_HEADER_PATTERN). */
export const UPLOAD_TICKET_HEADER_PATTERN = /^x-[a-z][a-z0-9]*-upload-ticket$/i;
/** A server reference code (Codelab.Observability ReferenceCode: 5 + 5 upper-case hex characters). */
export const REFERENCE_CODE_PATTERN = /\b[0-9A-F]{5}-[0-9A-F]{5}\b/;

/** The contact reference number: CU-YYYY-NNN (at least three digits). */
export const CONTACT_REFERENCE_PREFIX = 'CU';
export const CONTACT_REFERENCE_MIN_DIGITS = 3;

/** The app's browser storage key for the language (core/session/storage-keys.ts). */
export const LANGUAGE_STORAGE_KEY = 'starter.language';
export const FLOW_LANGUAGES = Object.freeze(['ar', 'en']);
export const FLOW_VIEWPORT = Object.freeze({ width: 1440, height: 900 });
