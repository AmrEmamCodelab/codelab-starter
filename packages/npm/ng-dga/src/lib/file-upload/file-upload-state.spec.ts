import { describe, expect, it } from 'vitest';
import { type DgaFileUploadItem, DgaFileUploadStatus } from './file-upload-item';
import {
  canMoveFileUpload,
  DGA_FILE_UPLOAD_TRANSITIONS,
  failedFileUpload,
  queuedFileUpload,
  requeuedFileUpload,
  uploadedFileUpload,
  uploadingFileUpload,
} from './file-upload-state';

const { Queued, Uploading, Uploaded, Failed } = DgaFileUploadStatus;
const REFERENCE = 'REF-1A2B3C';
const FAILURE = { errorText: 'The file is too large.', referenceCode: REFERENCE };

function item(status: DgaFileUploadStatus): DgaFileUploadItem {
  return { id: 'f1', name: 'scan.pdf', status, detail: '1 MB' };
}

describe('canMoveFileUpload', () => {
  it.each([
    [Queued, Uploading, true],
    [Queued, Failed, true],
    [Queued, Uploaded, false],
    [Uploading, Uploading, true],
    [Uploading, Uploaded, true],
    [Uploading, Failed, true],
    [Uploading, Queued, false],
    [Uploaded, Queued, false],
    [Uploaded, Uploading, false],
    [Uploaded, Failed, false],
    [Failed, Queued, true],
    [Failed, Uploading, false],
    [Failed, Uploaded, false],
  ] as const)('%s -> %s is %s', (from, to, allowed) => {
    expect(canMoveFileUpload(from, to)).toBe(allowed);
  });

  it('lists moves for every status', () => {
    expect(Object.keys(DGA_FILE_UPLOAD_TRANSITIONS).map(Number)).toEqual(Object.values(DgaFileUploadStatus));
  });
});

describe('queuedFileUpload', () => {
  it('starts a queued row with an optional detail', () => {
    expect(queuedFileUpload('f1', 'scan.pdf')).toEqual({ id: 'f1', name: 'scan.pdf', status: Queued, detail: null });
    expect(queuedFileUpload('f1', 'scan.pdf', '1 MB').detail).toBe('1 MB');
  });
});

describe('uploadingFileUpload', () => {
  it('moves a queued row to uploading with clamped progress', () => {
    expect(uploadingFileUpload(item(Queued), 33.3)).toMatchObject({ status: Uploading, progress: 33 });
    expect(uploadingFileUpload(item(Queued), 140).progress).toBe(100);
  });

  it('updates the progress of an uploading row, or makes it indeterminate', () => {
    expect(uploadingFileUpload({ ...item(Uploading), progress: 10 }, 60).progress).toBe(60);
    expect(uploadingFileUpload({ ...item(Uploading), progress: 10 }, null).progress).toBeNull();
  });

  it('ignores a late progress event on a finished row', () => {
    const failed = failedFileUpload(item(Uploading), FAILURE);
    expect(uploadingFileUpload(failed, 50)).toBe(failed);
    const uploaded = item(Uploaded);
    expect(uploadingFileUpload(uploaded, 50)).toBe(uploaded);
  });
});

describe('uploadedFileUpload', () => {
  it('finishes an uploading row, clearing progress and keeping or replacing the detail', () => {
    const uploading = { ...item(Uploading), progress: 99 };
    expect(uploadedFileUpload(uploading)).toMatchObject({ status: Uploaded, progress: null, detail: '1 MB', errorText: null, referenceCode: null });
    expect(uploadedFileUpload(uploading, '2 MB').detail).toBe('2 MB');
  });

  it('refuses to skip the upload', () => {
    const queued = item(Queued);
    expect(uploadedFileUpload(queued)).toBe(queued);
  });
});

describe('failedFileUpload', () => {
  it('fails an uploading or queued row with its reason and reference code', () => {
    expect(failedFileUpload({ ...item(Uploading), progress: 40 }, FAILURE)).toMatchObject({
      status: Failed,
      progress: null,
      errorText: FAILURE.errorText,
      referenceCode: REFERENCE,
    });
    expect(failedFileUpload(item(Queued), { errorText: 'Refused.' })).toMatchObject({ status: Failed, referenceCode: null });
  });

  it('never fails a stored row', () => {
    const uploaded = item(Uploaded);
    expect(failedFileUpload(uploaded, FAILURE)).toBe(uploaded);
  });
});

describe('requeuedFileUpload', () => {
  it('queues a failed row again with its error cleared', () => {
    expect(requeuedFileUpload(failedFileUpload(item(Uploading), FAILURE))).toMatchObject({
      status: Queued,
      errorText: null,
      referenceCode: null,
      progress: null,
    });
  });

  it('leaves other rows alone', () => {
    const uploading = item(Uploading);
    expect(requeuedFileUpload(uploading)).toBe(uploading);
  });
});
