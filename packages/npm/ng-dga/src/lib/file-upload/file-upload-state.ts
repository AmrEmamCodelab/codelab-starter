import { clampProgress, type DgaFileUploadItem, DgaFileUploadStatus } from './file-upload-item';

/**
 * The moves a file row may make. A row is queued when picked, uploads (any number of progress updates), then ends
 * uploaded or failed; a queued row may fail before it starts (the ticket was refused), and a failed row may be queued
 * again (retry). An uploaded row is final: replacing it is a remove and a new pick.
 */
export const DGA_FILE_UPLOAD_TRANSITIONS: Readonly<Record<DgaFileUploadStatus, readonly DgaFileUploadStatus[]>> = {
  [DgaFileUploadStatus.Queued]: [DgaFileUploadStatus.Uploading, DgaFileUploadStatus.Failed],
  [DgaFileUploadStatus.Uploading]: [DgaFileUploadStatus.Uploading, DgaFileUploadStatus.Uploaded, DgaFileUploadStatus.Failed],
  [DgaFileUploadStatus.Uploaded]: [],
  [DgaFileUploadStatus.Failed]: [DgaFileUploadStatus.Queued],
};

/** What a failed row shows (already translated): why, and the server's reference code when it gave one. */
export interface DgaFileUploadFailure {
  readonly errorText: string | null;
  readonly referenceCode?: string | null;
}

/** True when a row in `from` may move to `to`. */
export function canMoveFileUpload(from: DgaFileUploadStatus, to: DgaFileUploadStatus): boolean {
  return DGA_FILE_UPLOAD_TRANSITIONS[from].includes(to);
}

/** A new queued row for a picked file. */
export function queuedFileUpload(id: string, name: string, detail: string | null = null): DgaFileUploadItem {
  return { id, name, status: DgaFileUploadStatus.Queued, detail };
}

/**
 * The row uploading at `progress` percent (clamped and rounded; null for indeterminate). A row that may not upload
 * (already uploaded or failed) is returned unchanged, so a late progress event after a failure cannot revive it.
 */
export function uploadingFileUpload(item: DgaFileUploadItem, progress: number | null): DgaFileUploadItem {
  if (!canMoveFileUpload(item.status, DgaFileUploadStatus.Uploading)) return item;
  return { ...item, status: DgaFileUploadStatus.Uploading, progress: clampProgress(progress), errorText: null, referenceCode: null };
}

/** The row stored by the server; an optional new detail (such as the stored size). Unchanged unless it was uploading. */
export function uploadedFileUpload(item: DgaFileUploadItem, detail: string | null = item.detail ?? null): DgaFileUploadItem {
  if (!canMoveFileUpload(item.status, DgaFileUploadStatus.Uploaded)) return item;
  return { ...item, status: DgaFileUploadStatus.Uploaded, progress: null, detail, errorText: null, referenceCode: null };
}

/** The row refused or failed, with its translated reason and reference code. Unchanged unless queued or uploading. */
export function failedFileUpload(item: DgaFileUploadItem, failure: DgaFileUploadFailure): DgaFileUploadItem {
  if (!canMoveFileUpload(item.status, DgaFileUploadStatus.Failed)) return item;
  return {
    ...item,
    status: DgaFileUploadStatus.Failed,
    progress: null,
    errorText: failure.errorText,
    referenceCode: failure.referenceCode ?? null,
  };
}

/** A failed row queued again for a retry, its error cleared. Unchanged unless it failed. */
export function requeuedFileUpload(item: DgaFileUploadItem): DgaFileUploadItem {
  if (!canMoveFileUpload(item.status, DgaFileUploadStatus.Queued)) return item;
  return { ...item, status: DgaFileUploadStatus.Queued, progress: null, errorText: null, referenceCode: null };
}
