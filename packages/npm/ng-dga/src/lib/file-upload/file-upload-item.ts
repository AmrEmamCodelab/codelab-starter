import type { DgaIconName } from '../icon/dga-icon-names.generated';

/** Where one file of a <dga-file-upload> list stands. The caller (an upload adapter) owns and updates it. */
export const DgaFileUploadStatus = {
  /** Picked, not sent yet. */
  Pending: 'pending',
  /** Being sent; `progress` shows how far. */
  Uploading: 'uploading',
  /** Stored by the server. */
  Uploaded: 'uploaded',
  /** Refused or failed; `errorText` says why. */
  Failed: 'failed',
} as const;
export type DgaFileUploadStatus = (typeof DgaFileUploadStatus)[keyof typeof DgaFileUploadStatus];

/** One row of the file list (texts already translated). */
export interface DgaFileUploadItem {
  /** Stable id of the row (the caller's), emitted by `removed`. */
  readonly id: string;
  /** The file name, shown left-to-right isolated so it is never reordered inside Arabic text. */
  readonly name: string;
  readonly status: DgaFileUploadStatus;
  /** Upload progress in percent (0 to 100) while uploading; null for an indeterminate bar. */
  readonly progress?: number | null;
  /** A short detail under the name, such as the formatted size (already translated). */
  readonly detail?: string | null;
  /** Why the file failed (already translated). */
  readonly errorText?: string | null;
}

/** The highest progress value. */
export const DGA_FILE_UPLOAD_PROGRESS_MAX = 100;

/** The placeholder a label template may use for the file name, e.g. "Remove {name}". */
export const DGA_FILE_NAME_PLACEHOLDER = '{name}';

const STATUS_ICONS: Readonly<Record<DgaFileUploadStatus, DgaIconName>> = {
  [DgaFileUploadStatus.Pending]: 'file',
  [DgaFileUploadStatus.Uploading]: 'upload',
  [DgaFileUploadStatus.Uploaded]: 'success',
  [DgaFileUploadStatus.Failed]: 'error',
};

export function fileStatusIcon(status: DgaFileUploadStatus): DgaIconName {
  return STATUS_ICONS[status];
}

/** The progress clamped to 0..100 and rounded, or null when it is unknown (an indeterminate bar). */
export function clampProgress(progress: number | null | undefined): number | null {
  if (progress === null || progress === undefined || Number.isNaN(progress)) return null;
  return Math.round(Math.min(DGA_FILE_UPLOAD_PROGRESS_MAX, Math.max(0, progress)));
}

/** A label template with `{name}` replaced by the file name; a template without the placeholder gets the name appended. */
export function fileLabel(template: string, name: string): string {
  return template.includes(DGA_FILE_NAME_PLACEHOLDER) ? template.split(DGA_FILE_NAME_PLACEHOLDER).join(name) : `${template} ${name}`;
}
