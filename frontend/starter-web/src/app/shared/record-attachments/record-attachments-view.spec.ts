import { DgaFileRejectionReason, DgaFileUploadStatus, DgaPageFeedbackKind } from '@codelab/ng-dga';
import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../core/i18n/language';
import { AttachmentError } from '../attachments/attachment-error';
import { testFile } from '../attachments/attachments.testing';
import { RecordAttachmentsTranslationKeys as Keys } from './record-attachments-keys';
import {
  AttachmentAction,
  attachmentSections,
  errorTextKey,
  failedUpload,
  failureFeedback,
  feedbackState,
  formatFileSize,
  isUploadRowId,
  queuedUpload,
  rejectionText,
  statusLabels,
  successFeedback,
  typeName,
  uploadRowId,
  uploadingUpload,
  type SectionSource,
} from './record-attachments-view';
import { OPTIONAL_TYPE, TEST_TYPE, TEST_REFERENCE_CODE, anAttachment, tooLarge } from './record-attachments.testing';

/** Echoes the key and its params, so the specs see exactly what would be translated. */
const echo = (key: string, params?: Readonly<Record<string, string | number>>): string =>
  params ? `${key}${JSON.stringify(params)}` : key;
const hasAll = (): boolean => true;
const hasNone = (): boolean => false;

function source(overrides: Partial<SectionSource> = {}): SectionSource {
  return {
    types: [TEST_TYPE, OPTIONAL_TYPE],
    attachments: [],
    uploads: [],
    missingTypeKeys: [],
    language: AppLanguages.English,
    translate: echo,
    has: hasAll,
    ...overrides,
  };
}

describe('upload rows', () => {
  it('ids picked files apart from linked attachments', () => {
    expect(uploadRowId(3)).toBe('upload-3');
    expect(isUploadRowId(uploadRowId(1))).toBe(true);
    expect(isUploadRowId(anAttachment().id)).toBe(false);
  });

  it('moves queued → uploading → failed, and never revives a failed row', () => {
    const queued = queuedUpload(uploadRowId(1), TEST_TYPE.key, testFile());
    expect(queued).toMatchObject({ status: DgaFileUploadStatus.Queued, fileName: 'report.pdf', sizeBytes: 4, error: null });
    const uploading = uploadingUpload(queued, 40);
    expect(uploading).toMatchObject({ status: DgaFileUploadStatus.Uploading, progress: 40 });
    const failed = failedUpload(uploading, tooLarge());
    expect(failed).toMatchObject({ status: DgaFileUploadStatus.Failed, progress: null });
    expect(failed.error?.referenceCode).toBe(TEST_REFERENCE_CODE);
    expect(uploadingUpload(failed, 90)).toBe(failed);
  });

  it('fails a queued row (a refused ticket), but never re-fails a failed row', () => {
    const queued = queuedUpload(uploadRowId(1), TEST_TYPE.key, testFile());
    const failed = failedUpload(queued, tooLarge());
    expect(failed.status).toBe(DgaFileUploadStatus.Failed);
    expect(failedUpload(failed, new AttachmentError('ATTACHMENTS_TICKET_INVALID', null, 401))).toBe(failed);
  });
});

describe('error texts and feedback', () => {
  it('renders errors.<CODE> when translated, the generic error otherwise', () => {
    expect(errorTextKey('ATTACHMENTS_FILE_TOO_LARGE', hasAll)).toBe('errors.ATTACHMENTS_FILE_TOO_LARGE');
    expect(errorTextKey('SOMETHING_NEW', hasNone)).toBe('errors.INTERNAL_ERROR');
  });

  it('builds a failure banner with the action title, the code text and the reference code', () => {
    const feedback = failureFeedback(AttachmentAction.Upload, tooLarge(), hasAll);
    expect(feedback).toEqual({
      kind: DgaPageFeedbackKind.Error,
      titleKey: Keys.FailedUpload,
      messageKey: 'errors.ATTACHMENTS_FILE_TOO_LARGE',
      referenceCode: TEST_REFERENCE_CODE,
    });
    expect(failureFeedback(AttachmentAction.Load, tooLarge(), hasAll).titleKey).toBe(Keys.FailedLoad);
    expect(failureFeedback(AttachmentAction.Download, tooLarge(), hasAll).titleKey).toBe(Keys.FailedDownload);
    expect(failureFeedback(AttachmentAction.Remove, tooLarge(), hasAll).titleKey).toBe(Keys.FailedRemove);
  });

  it('appends the isolated reference code to the message, and leaves it out when there is none', () => {
    const state = feedbackState(failureFeedback(AttachmentAction.Upload, tooLarge(), hasAll), echo);
    expect(state?.kind).toBe(DgaPageFeedbackKind.Error);
    expect(state?.title).toBe(Keys.FailedUpload);
    expect(state?.message).toContain(Keys.WithReference);
    expect(state?.message).toContain(`⁨${TEST_REFERENCE_CODE}⁩`);
    const noReference = new AttachmentError('NETWORK', null, 0);
    expect(feedbackState(failureFeedback(AttachmentAction.Load, noReference, hasAll), echo)?.message).toBe('errors.NETWORK');
    expect(feedbackState(null, echo)).toBeNull();
  });

  it('builds a success banner without a title', () => {
    const state = feedbackState(successFeedback(Keys.Uploaded, 'a.pdf'), echo);
    expect(state).toEqual({ kind: DgaPageFeedbackKind.Success, title: null, message: `${Keys.Uploaded}{"name":"a.pdf"}` });
  });

  it('turns client refusals into one translated line, nothing when none', () => {
    const file = testFile('virus.exe');
    expect(rejectionText([], 1, echo)).toBeNull();
    expect(
      rejectionText(
        [
          { file, reason: DgaFileRejectionReason.Type },
          { file, reason: DgaFileRejectionReason.Count },
        ],
        2,
        echo,
      ),
    ).toBe(`${Keys.RejectedType}{"name":"virus.exe","max":2} ${Keys.RejectedCount}{"name":"virus.exe","max":2}`);
    expect(rejectionText([{ file, reason: DgaFileRejectionReason.Empty }], 1, echo)).toContain(Keys.RejectedEmpty);
    expect(rejectionText([{ file, reason: DgaFileRejectionReason.Size }], 1, echo)).toContain(Keys.RejectedSize);
  });
});

describe('labels', () => {
  it('names the type in the current language', () => {
    expect(typeName(TEST_TYPE, AppLanguages.Arabic)).toBe(TEST_TYPE.nameAr);
    expect(typeName(TEST_TYPE, AppLanguages.English)).toBe(TEST_TYPE.nameEn);
  });

  it('formats sizes in the largest whole unit', () => {
    expect(formatFileSize(512, AppLanguages.English)).toMatch(/^512\s?(byte|B)/);
    expect(formatFileSize(1536, AppLanguages.English)).toMatch(/^1\.5\s?kB/);
    expect(formatFileSize(5 * 1024 * 1024, AppLanguages.English)).toMatch(/^5\s?MB/);
    expect(formatFileSize(1536, AppLanguages.Arabic)).not.toBe(formatFileSize(1536, AppLanguages.English));
  });

  it('gives every status an announcement', () => {
    expect(statusLabels(echo)).toEqual({
      [DgaFileUploadStatus.Queued]: Keys.StatusQueued,
      [DgaFileUploadStatus.Uploading]: Keys.StatusUploading,
      [DgaFileUploadStatus.Uploaded]: Keys.StatusUploaded,
      [DgaFileUploadStatus.Failed]: Keys.StatusFailed,
    });
  });
});

describe('attachmentSections', () => {
  it('makes one section per type, in the server order, with its limits and hint', () => {
    const sections = attachmentSections(source());
    expect(sections.map((section) => section.key)).toEqual([TEST_TYPE.key, OPTIONAL_TYPE.key]);
    expect(sections[0]).toMatchObject({
      label: TEST_TYPE.nameEn,
      accept: '.pdf,.png,.jpg,.jpeg',
      maxFiles: 5,
      required: true,
      missingText: null,
      items: [],
    });
    expect(sections[0]?.hint).toBe(`${Keys.Hint}{"extensions":".pdf, .png, .jpg, .jpeg","max":5}`);
  });

  it('shows the missing text only for the types the server lists as missing', () => {
    const sections = attachmentSections(source({ missingTypeKeys: [TEST_TYPE.key] }));
    expect(sections[0]?.missingText).toBe(Keys.Missing);
    expect(sections[1]?.missingText).toBeNull();
  });

  it('lists linked attachments as uploaded rows, then the picked files of the same type', () => {
    const linked = anAttachment({ fileName: 'linked.pdf', sizeBytes: 2048 });
    const other = anAttachment({ typeKey: OPTIONAL_TYPE.key, fileName: 'other.pdf' });
    const failed = failedUpload(queuedUpload(uploadRowId(1), TEST_TYPE.key, testFile('big.pdf')), tooLarge());
    const [demo, optional] = attachmentSections(source({ attachments: [linked, other], uploads: [failed] }));
    expect(demo?.items).toEqual([
      { id: linked.id, name: 'linked.pdf', status: DgaFileUploadStatus.Uploaded, detail: formatFileSize(2048, AppLanguages.English) },
      {
        id: failed.id,
        name: 'big.pdf',
        status: DgaFileUploadStatus.Failed,
        progress: null,
        detail: formatFileSize(4, AppLanguages.English),
        errorText: 'errors.ATTACHMENTS_FILE_TOO_LARGE',
        referenceCode: TEST_REFERENCE_CODE,
      },
    ]);
    expect(optional?.items.map((item) => item.name)).toEqual(['other.pdf']);
  });

  it('falls back to the generic error text on a row whose code is not translated', () => {
    const failed = failedUpload(queuedUpload(uploadRowId(1), TEST_TYPE.key, testFile()), new AttachmentError('NEW_CODE', null, 400));
    const [demo] = attachmentSections(source({ uploads: [failed], has: hasNone }));
    expect(demo?.items[0]).toMatchObject({ errorText: 'errors.INTERNAL_ERROR', referenceCode: null });
  });
});
