import type { DgaIconName } from '../icon/dga-icon-names.generated';

/**
 * Where one file of a <dga-file-upload> list stands. The caller (an upload adapter) owns and updates it, ideally
 * through the transition helpers in `file-upload-state.ts`. Integer values, explicit on every member.
 */
export const DgaFileUploadStatus = {
  /** Picked and accepted by the client checks, not sent yet. */
  Queued: 0,
  /** Being sent; `progress` shows how far (0 to 100, or null for an indeterminate bar). */
  Uploading: 1,
  /** Stored by the server. */
  Uploaded: 2,
  /** Refused or failed; `errorText` says why and `referenceCode` names the server's record of it. */
  Failed: 3,
} as const;
export type DgaFileUploadStatus = (typeof DgaFileUploadStatus)[keyof typeof DgaFileUploadStatus];

/** One row of the file list (texts already translated). */
export interface DgaFileUploadItem {
  /** Stable id of the row (the caller's). */
  readonly id: string;
  /** The file name, shown isolated (`<bdi>`) so it is never reordered inside Arabic text. */
  readonly name: string;
  readonly status: DgaFileUploadStatus;
  /** Upload progress in percent (0 to 100) while uploading; null for an indeterminate bar. */
  readonly progress?: number | null;
  /** A short detail under the name, such as the formatted size (already translated). */
  readonly detail?: string | null;
  /** Why the file failed (already translated); shown on a failed row. */
  readonly errorText?: string | null;
  /** The server's reference code for the failure (problem+json `referenceCode`); shown on a failed row, left-to-right. */
  readonly referenceCode?: string | null;
}

/** The highest progress value. */
export const DGA_FILE_UPLOAD_PROGRESS_MAX = 100;

/** The lowest progress value. */
export const DGA_FILE_UPLOAD_PROGRESS_MIN = 0;

/** The placeholder a label template may use for the file name, e.g. "Remove {name}". */
export const DGA_FILE_NAME_PLACEHOLDER = '{name}';

/** The `data-status` value of a row per status: the stable styling and test hook (the status itself is an integer). */
export const DGA_FILE_UPLOAD_STATUS_NAMES: Readonly<Record<DgaFileUploadStatus, string>> = {
  [DgaFileUploadStatus.Queued]: 'queued',
  [DgaFileUploadStatus.Uploading]: 'uploading',
  [DgaFileUploadStatus.Uploaded]: 'uploaded',
  [DgaFileUploadStatus.Failed]: 'failed',
};

const STATUS_ICONS: Readonly<Record<DgaFileUploadStatus, DgaIconName>> = {
  [DgaFileUploadStatus.Queued]: 'file',
  [DgaFileUploadStatus.Uploading]: 'upload',
  [DgaFileUploadStatus.Uploaded]: 'success',
  [DgaFileUploadStatus.Failed]: 'error',
};

export function fileStatusIcon(status: DgaFileUploadStatus): DgaIconName {
  return STATUS_ICONS[status];
}

/** The `data-status` name of a status. */
export function fileStatusName(status: DgaFileUploadStatus): string {
  return DGA_FILE_UPLOAD_STATUS_NAMES[status];
}

/** The progress clamped to 0..100 and rounded, or null when it is unknown (an indeterminate bar). */
export function clampProgress(progress: number | null | undefined): number | null {
  if (progress === null || progress === undefined || Number.isNaN(progress)) return null;
  return Math.round(Math.min(DGA_FILE_UPLOAD_PROGRESS_MAX, Math.max(DGA_FILE_UPLOAD_PROGRESS_MIN, progress)));
}

/** A label template with `{name}` replaced by the file name; a template without the placeholder gets the name appended. */
export function fileLabel(template: string, name: string): string {
  return template.includes(DGA_FILE_NAME_PLACEHOLDER) ? template.split(DGA_FILE_NAME_PLACEHOLDER).join(name) : `${template} ${name}`;
}
