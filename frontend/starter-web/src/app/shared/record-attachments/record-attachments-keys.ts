import { AttachmentErrorCodes } from '../attachments/attachment-error-codes';

/** The texts of `app-record-attachments`, under `attachments.widget.*` in both bundles. */
export const RecordAttachmentsTranslationKeys = {
  Loading: 'attachments.widget.loading',
  LoadFailed: 'attachments.widget.load-failed',
  Retry: 'attachments.widget.retry',
  NoTypes: 'attachments.widget.no-types',
  Browse: 'attachments.widget.browse',
  Drop: 'attachments.widget.drop',
  /** `{{extensions}}` and `{{max}}`. */
  Hint: 'attachments.widget.hint',
  Required: 'attachments.widget.required',
  Missing: 'attachments.widget.missing',
  ReferenceCodeLabel: 'attachments.widget.reference-code',
  /** ng-dga templates: `{name}` is filled in by `dga-file-upload`. */
  Download: 'attachments.widget.download',
  Remove: 'attachments.widget.remove',
  StatusQueued: 'attachments.widget.status-queued',
  StatusUploading: 'attachments.widget.status-uploading',
  StatusUploaded: 'attachments.widget.status-uploaded',
  StatusFailed: 'attachments.widget.status-failed',
  /** `{{name}}`; `{{max}}` for the count. */
  RejectedType: 'attachments.widget.rejected-type',
  RejectedSize: 'attachments.widget.rejected-size',
  RejectedEmpty: 'attachments.widget.rejected-empty',
  RejectedCount: 'attachments.widget.rejected-count',
  /** Page feedback titles, one per action. */
  FailedLoad: 'attachments.widget.failed-load',
  FailedUpload: 'attachments.widget.failed-upload',
  FailedDownload: 'attachments.widget.failed-download',
  FailedRemove: 'attachments.widget.failed-remove',
  /** `{{name}}`. */
  Uploaded: 'attachments.widget.uploaded',
  Removed: 'attachments.widget.removed',
  /** `{{message}}` followed by `{{code}}`: the error text with its reference code. */
  WithReference: 'attachments.widget.with-reference',
  Dismiss: 'attachments.widget.dismiss',
} as const;

/** Prefix of the per-code error texts (`errors.<CODE>`), shared with the app-wide error dialog. */
export const ERROR_TEXT_PREFIX = 'errors.';

/** Every `errors.<CODE>` key the attachment flow renders (contract sections 1 and 3), for the bundle check. */
export const ATTACHMENT_ERROR_TEXT_KEYS: readonly string[] = Object.values(AttachmentErrorCodes).map(
  (code) => `${ERROR_TEXT_PREFIX}${code}`,
);

/** Every key the widget renders, for the bundle check. */
export const RECORD_ATTACHMENTS_TRANSLATION_KEYS: readonly string[] = [
  ...Object.values(RecordAttachmentsTranslationKeys),
  ...ATTACHMENT_ERROR_TEXT_KEYS,
];

/** The stable `data-testid` hooks of the widget (specs and Playwright use them). */
export const RecordAttachmentsTestIds = {
  Root: 'record-attachments',
  Loading: 'record-attachments-loading',
  Retry: 'record-attachments-retry',
  NoTypes: 'record-attachments-no-types',
  /** One per attachment type: `record-attachments-type`, with `data-type-key` naming the type. */
  Type: 'record-attachments-type',
} as const;
