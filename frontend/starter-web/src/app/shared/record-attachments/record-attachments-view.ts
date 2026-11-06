import { ApiErrorCodes } from '@codelab/ng-security';
import {
  canMoveFileUpload,
  DgaFileRejectionReason,
  DgaFileUploadStatus,
  DgaPageFeedbackKind,
  type DgaFileRejection,
  type DgaFileUploadItem,
  type DgaFileUploadStatusLabels,
  type DgaPageFeedbackState,
} from '@codelab/ng-dga';
import { AppLanguages, type AppLanguage } from '../../core/i18n/language';
import type { TranslationParams } from '../../core/i18n/translation.service';
import type { AttachmentError } from '../attachments/attachment-error';
import { acceptAttributeFor } from '../attachments/attachment-files';
import type { AttachmentType, RecordAttachment } from '../attachments/attachments.models';
import { ERROR_TEXT_PREFIX, RecordAttachmentsTranslationKeys as Keys } from './record-attachments-keys';

/** Looks up a translation (the widget passes `TranslationService.translate`). */
export type Translate = (key: string, params?: TranslationParams) => string;

/** Where the widget's first load stands. Integer values, explicit on every member. */
export enum RecordAttachmentsLoadState {
  Loading = 0,
  Ready = 1,
  Failed = 2,
}

/** The action a page-feedback message reports on. */
export enum AttachmentAction {
  Load = 0,
  Upload = 1,
  Download = 2,
  Remove = 3,
}

/** One file the user picked, on its own trip to the attachment service (never a linked attachment). */
export interface UploadRow {
  /** The row's own id (`upload-<n>`), never the file name: two files may share a name. */
  readonly id: string;
  readonly typeKey: string;
  readonly fileName: string;
  readonly sizeBytes: number;
  readonly status: DgaFileUploadStatus;
  /** 0..100 while uploading; null for an indeterminate bar. */
  readonly progress: number | null;
  /** Why it failed (set exactly when `status` is Failed). */
  readonly error: AttachmentError | null;
}

/** What the page-feedback banner says, as keys: the widget translates it in the current language. */
export interface AttachmentsFeedback {
  readonly kind: DgaPageFeedbackKind;
  readonly titleKey: string | null;
  readonly messageKey: string;
  readonly params?: TranslationParams;
  readonly referenceCode: string | null;
}

/** Everything one `dga-file-upload` needs for one attachment type (texts translated). */
export interface AttachmentTypeSection {
  readonly key: string;
  readonly label: string;
  readonly hint: string;
  readonly accept: string;
  readonly maxFiles: number;
  readonly required: boolean;
  /** The server's verdict: shown only while the server lists the type as missing. */
  readonly missingText: string | null;
  readonly items: readonly DgaFileUploadItem[];
}

/** What {@link attachmentSections} reads. */
export interface SectionSource {
  readonly types: readonly AttachmentType[];
  readonly attachments: readonly RecordAttachment[];
  readonly uploads: readonly UploadRow[];
  readonly missingTypeKeys: readonly string[];
  readonly language: AppLanguage;
  readonly translate: Translate;
  /** Whether the bundle has a key (`TranslationService.has`). */
  readonly has: (key: string) => boolean;
}

const EXTENSION_LIST_SEPARATOR = ', ';
const UPLOAD_ROW_PREFIX = 'upload-';
/** First Strong Isolate / Pop Directional Isolate: keeps a Latin reference code whole inside Arabic text. */
const ISOLATE_START = '⁨';
const ISOLATE_END = '⁩';

const SIZE_FRACTION_DIGITS = 1;
const BYTES_PER_KILOBYTE = 1024;
const BYTE_UNIT = { unit: 'byte', bytes: 1 } as const;
/** Largest first. */
const FILE_SIZE_UNITS: readonly { readonly unit: string; readonly bytes: number }[] = [
  { unit: 'gigabyte', bytes: BYTES_PER_KILOBYTE * BYTES_PER_KILOBYTE * BYTES_PER_KILOBYTE },
  { unit: 'megabyte', bytes: BYTES_PER_KILOBYTE * BYTES_PER_KILOBYTE },
  { unit: 'kilobyte', bytes: BYTES_PER_KILOBYTE },
  BYTE_UNIT,
];

const REJECTION_KEYS: Readonly<Record<DgaFileRejectionReason, string>> = {
  [DgaFileRejectionReason.Type]: Keys.RejectedType,
  [DgaFileRejectionReason.Size]: Keys.RejectedSize,
  [DgaFileRejectionReason.Empty]: Keys.RejectedEmpty,
  [DgaFileRejectionReason.Count]: Keys.RejectedCount,
};

const FAILURE_TITLES: Readonly<Record<AttachmentAction, string>> = {
  [AttachmentAction.Load]: Keys.FailedLoad,
  [AttachmentAction.Upload]: Keys.FailedUpload,
  [AttachmentAction.Download]: Keys.FailedDownload,
  [AttachmentAction.Remove]: Keys.FailedRemove,
};

/** The row id of the `sequence`-th picked file. */
export function uploadRowId(sequence: number): string {
  return `${UPLOAD_ROW_PREFIX}${sequence}`;
}

/** Whether a `dga-file-upload` row id names a picked file (not a linked attachment). */
export function isUploadRowId(id: string): boolean {
  return id.startsWith(UPLOAD_ROW_PREFIX);
}

/** A picked file waiting for its turn. */
export function queuedUpload(id: string, typeKey: string, file: File): UploadRow {
  return { id, typeKey, fileName: file.name, sizeBytes: file.size, status: DgaFileUploadStatus.Queued, progress: null, error: null };
}

/**
 * Bytes are on their way; `percent` is null while the total is unknown. A move ng-dga's transition table refuses
 * (`canMoveFileUpload`) leaves the row unchanged, so a late progress event never revives a failed row.
 */
export function uploadingUpload(row: UploadRow, percent: number | null): UploadRow {
  if (!canMoveFileUpload(row.status, DgaFileUploadStatus.Uploading)) return row;
  return { ...row, status: DgaFileUploadStatus.Uploading, progress: percent };
}

/** The service or the API refused the file. A row that is already final is left unchanged. */
export function failedUpload(row: UploadRow, error: AttachmentError): UploadRow {
  if (!canMoveFileUpload(row.status, DgaFileUploadStatus.Failed)) return row;
  return { ...row, status: DgaFileUploadStatus.Failed, progress: null, error };
}

/** `errors.<CODE>` when the bundle has it, otherwise the generic `errors.INTERNAL_ERROR` (never a raw code). */
export function errorTextKey(code: string, has: (key: string) => boolean): string {
  const key = `${ERROR_TEXT_PREFIX}${code}`;
  return has(key) ? key : `${ERROR_TEXT_PREFIX}${ApiErrorCodes.internalError}`;
}

/** The failure banner for `action`: its title, the code's text and the reference code. */
export function failureFeedback(action: AttachmentAction, error: AttachmentError, has: (key: string) => boolean): AttachmentsFeedback {
  return {
    kind: DgaPageFeedbackKind.Error,
    titleKey: FAILURE_TITLES[action],
    messageKey: errorTextKey(error.code, has),
    referenceCode: error.referenceCode,
  };
}

/** A success banner (`attachments.widget.uploaded` / `removed`) naming the file. */
export function successFeedback(messageKey: string, fileName: string): AttachmentsFeedback {
  return { kind: DgaPageFeedbackKind.Success, titleKey: null, messageKey, params: { name: fileName }, referenceCode: null };
}

/** The banner in the current language; the reference code is appended and isolated so it reads left to right. */
export function feedbackState(feedback: AttachmentsFeedback | null, translate: Translate): DgaPageFeedbackState | null {
  if (feedback === null) return null;
  const message = translate(feedback.messageKey, feedback.params);
  return {
    kind: feedback.kind,
    title: feedback.titleKey === null ? null : translate(feedback.titleKey),
    message:
      feedback.referenceCode === null
        ? message
        : translate(Keys.WithReference, { message, code: `${ISOLATE_START}${feedback.referenceCode}${ISOLATE_END}` }),
  };
}

/** The client pre-check refusals of one pick, as one translated line (null when nothing was refused). */
export function rejectionText(rejected: readonly DgaFileRejection[], maxFiles: number, translate: Translate): string | null {
  if (rejected.length === 0) return null;
  return rejected
    .map((rejection) => translate(REJECTION_KEYS[rejection.reason], { name: rejection.file.name, max: maxFiles }))
    .join(' ');
}

/** The type's name in `language`. */
export function typeName(type: AttachmentType, language: AppLanguage): string {
  return language === AppLanguages.Arabic ? type.nameAr : type.nameEn;
}

/** A size such as "1.2 MB" / "١٫٢ ميغابايت", formatted by the browser in `language`. */
export function formatFileSize(bytes: number, language: AppLanguage): string {
  const unit = FILE_SIZE_UNITS.find((candidate) => bytes >= candidate.bytes) ?? BYTE_UNIT;
  const format = new Intl.NumberFormat(language, { style: 'unit', unit: unit.unit, maximumFractionDigits: SIZE_FRACTION_DIGITS });
  return format.format(bytes / unit.bytes);
}

/** The translated status announcements of the rows (`{name}` is filled in by `dga-file-upload`). */
export function statusLabels(translate: Translate): DgaFileUploadStatusLabels {
  return {
    [DgaFileUploadStatus.Queued]: translate(Keys.StatusQueued),
    [DgaFileUploadStatus.Uploading]: translate(Keys.StatusUploading),
    [DgaFileUploadStatus.Uploaded]: translate(Keys.StatusUploaded),
    [DgaFileUploadStatus.Failed]: translate(Keys.StatusFailed),
  };
}

/** The rows of one type: its linked attachments (uploaded), then the files still on their way or refused. */
export function typeRows(typeKey: string, source: SectionSource): DgaFileUploadItem[] {
  const linked = source.attachments
    .filter((attachment) => attachment.typeKey === typeKey)
    .map<DgaFileUploadItem>((attachment) => ({
      id: attachment.id,
      name: attachment.fileName,
      status: DgaFileUploadStatus.Uploaded,
      detail: formatFileSize(attachment.sizeBytes, source.language),
    }));
  const picked = source.uploads
    .filter((row) => row.typeKey === typeKey)
    .map<DgaFileUploadItem>((row) => ({
      id: row.id,
      name: row.fileName,
      status: row.status,
      progress: row.progress,
      detail: formatFileSize(row.sizeBytes, source.language),
      errorText: row.error ? source.translate(errorTextKey(row.error.code, source.has)) : null,
      referenceCode: row.error?.referenceCode ?? null,
    }));
  return [...linked, ...picked];
}

/** One section per attachment type, in the server's order. */
export function attachmentSections(source: SectionSource): AttachmentTypeSection[] {
  const missing = new Set(source.missingTypeKeys);
  return source.types.map((type) => ({
    key: type.key,
    label: typeName(type, source.language),
    hint: source.translate(Keys.Hint, {
      extensions: type.allowedExtensions.join(EXTENSION_LIST_SEPARATOR),
      max: type.maxFiles,
    }),
    accept: acceptAttributeFor(type.allowedExtensions),
    maxFiles: type.maxFiles,
    required: type.isRequired,
    missingText: missing.has(type.key) ? source.translate(Keys.Missing) : null,
    items: typeRows(type.key, source),
  }));
}
