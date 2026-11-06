import { TestBed } from '@angular/core/testing';
import { DgaFileUploadStatus, DgaPageFeedbackKind } from '@codelab/ng-dga';
import { Subject, throwError } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { AttachmentError } from '../attachments/attachment-error';
import { AttachmentUploadEventKind } from '../attachments/attachments.models';
import { TEST_RECORD_ID, TEST_RECORD_TYPE, TEST_TYPE_KEY, testFile } from '../attachments/attachments.testing';
import { RecordAttachmentsTranslationKeys as Keys } from './record-attachments-keys';
import { RecordAttachmentsLoadState } from './record-attachments-view';
import { RecordAttachmentsStore } from './record-attachments.store';
import {
  TEST_REFERENCE_CODE,
  aList,
  anAttachment,
  fakeAdapter,
  recordAttachmentsTestProviders,
  tooLarge,
  useLanguage,
  type FakeAdapter,
} from './record-attachments.testing';
import { AppLanguages } from '../../core/i18n/language';

let adapter: FakeAdapter;
let store: RecordAttachmentsStore;

async function setUp(next: FakeAdapter = fakeAdapter()): Promise<void> {
  adapter = next;
  TestBed.configureTestingModule({ providers: [...recordAttachmentsTestProviders(adapter), RecordAttachmentsStore] });
  await useLanguage(AppLanguages.Arabic);
  store = TestBed.inject(RecordAttachmentsStore);
}

describe('RecordAttachmentsStore', () => {
  beforeEach(async () => setUp());

  it('loads the types and the list of the record', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    expect(adapter.types).toHaveBeenCalledWith(TEST_RECORD_TYPE);
    expect(adapter.list).toHaveBeenCalledWith(TEST_RECORD_TYPE, TEST_RECORD_ID);
    expect(store.loadState()).toBe(RecordAttachmentsLoadState.Ready);
    expect(store.types().map((type) => type.key)).toEqual([TEST_TYPE_KEY]);
    expect(store.missingTypeKeys()).toEqual([TEST_TYPE_KEY]);
  });

  it('reports a failed load with its code and reference code, and retries', () => {
    adapter.list.mockReturnValueOnce(throwError(() => new AttachmentError('ATTACHMENTS_STORAGE_UNAVAILABLE', TEST_REFERENCE_CODE, 503)));
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    expect(store.loadState()).toBe(RecordAttachmentsLoadState.Failed);
    expect(store.feedback()).toMatchObject({
      titleKey: Keys.FailedLoad,
      messageKey: 'errors.ATTACHMENTS_STORAGE_UNAVAILABLE',
      referenceCode: TEST_REFERENCE_CODE,
    });
    store.retry();
    expect(store.loadState()).toBe(RecordAttachmentsLoadState.Ready);
    expect(store.feedback()).toBeNull();
  });

  it('uploads picked files one at a time, shows progress, then lists the linked file and refreshes', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    const linked = anAttachment({ fileName: 'a.pdf' });
    store.upload(TEST_TYPE_KEY, [testFile('a.pdf'), testFile('b.pdf')]);

    expect(adapter.upload).toHaveBeenCalledTimes(1);
    expect(adapter.upload).toHaveBeenCalledWith(TEST_RECORD_TYPE, TEST_RECORD_ID, TEST_TYPE_KEY, expect.any(File));
    expect(store.uploads().map((row) => row.status)).toEqual([DgaFileUploadStatus.Queued, DgaFileUploadStatus.Queued]);

    adapter.uploads[0]?.next({ kind: AttachmentUploadEventKind.Progress, loadedBytes: 2, totalBytes: 4, percent: 50 });
    expect(store.uploads()[0]).toMatchObject({ status: DgaFileUploadStatus.Uploading, progress: 50 });

    adapter.list.mockClear();
    adapter.uploads[0]?.next({ kind: AttachmentUploadEventKind.Linked, attachment: linked });
    adapter.uploads[0]?.complete();
    expect(store.uploads().map((row) => row.fileName)).toEqual(['b.pdf']);
    expect(store.feedback()).toMatchObject({ kind: DgaPageFeedbackKind.Success, messageKey: Keys.Uploaded, params: { name: 'a.pdf' } });
    expect(adapter.list).toHaveBeenCalledTimes(1);
    expect(adapter.upload).toHaveBeenCalledTimes(2);
  });

  it('shows an unknown total as an indeterminate bar', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.upload(TEST_TYPE_KEY, [testFile()]);
    adapter.uploads[0]?.next({ kind: AttachmentUploadEventKind.Progress, loadedBytes: 2, totalBytes: null, percent: 0 });
    expect(store.uploads()[0]).toMatchObject({ status: DgaFileUploadStatus.Uploading, progress: null });
  });

  it('marks a refused file as failed with its reference code, reports it, and goes on with the next one', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.upload(TEST_TYPE_KEY, [testFile('big.pdf'), testFile('small.pdf')]);
    adapter.uploads[0]?.error(tooLarge());
    expect(store.uploads()[0]).toMatchObject({ status: DgaFileUploadStatus.Failed, fileName: 'big.pdf' });
    expect(store.uploads()[0]?.error?.referenceCode).toBe(TEST_REFERENCE_CODE);
    expect(store.feedback()).toMatchObject({ titleKey: Keys.FailedUpload, messageKey: 'errors.ATTACHMENTS_FILE_TOO_LARGE' });
    expect(adapter.upload).toHaveBeenCalledTimes(2);
  });

  it('ignores an empty pick and a pick before any record is loaded', () => {
    store.upload(TEST_TYPE_KEY, [testFile()]);
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.upload(TEST_TYPE_KEY, []);
    expect(adapter.upload).not.toHaveBeenCalled();
    expect(store.uploads()).toEqual([]);
  });

  it('cancels the running uploads when another record is loaded', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.upload(TEST_TYPE_KEY, [testFile()]);
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    expect(adapter.uploads[0]?.observed).toBe(false);
    expect(store.uploads()).toEqual([]);
  });

});

describe('RecordAttachmentsStore with a linked attachment', () => {
  const attachment = anAttachment();

  beforeEach(async () => setUp(fakeAdapter(undefined, aList([attachment]))));

  it('downloads a linked attachment and ignores unknown ids', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.download('unknown');
    expect(adapter.download).not.toHaveBeenCalled();
    store.download(attachment.id);
    expect(adapter.download).toHaveBeenCalledWith(attachment);
    expect(store.downloadingId()).toBeNull();
  });

  it('runs one download at a time', () => {
    const running = new Subject<void>();
    adapter.download.mockReturnValueOnce(running);
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.download(attachment.id);
    expect(store.downloadingId()).toBe(attachment.id);
    store.download(attachment.id);
    expect(adapter.download).toHaveBeenCalledTimes(1);
    running.complete();
    expect(store.downloadingId()).toBeNull();
  });

  it('reports a refused download with its reference code', () => {
    adapter.download.mockReturnValueOnce(throwError(() => new AttachmentError('ATTACHMENTS_NOT_FOUND', TEST_REFERENCE_CODE, 404)));
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.download(attachment.id);
    expect(store.feedback()).toMatchObject({ titleKey: Keys.FailedDownload, messageKey: 'errors.ATTACHMENTS_NOT_FOUND', referenceCode: TEST_REFERENCE_CODE });
    expect(store.downloadingId()).toBeNull();
  });

  it('removes a linked attachment on the server, then reloads the list for the missing verdict', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    adapter.list.mockClear();
    store.remove(attachment.id);
    expect(adapter.remove).toHaveBeenCalledWith(attachment.id);
    expect(store.feedback()).toMatchObject({ kind: DgaPageFeedbackKind.Success, messageKey: Keys.Removed });
    expect(adapter.list).toHaveBeenCalledTimes(1);
  });

  it('reports a refused remove and keeps the attachment', () => {
    adapter.remove.mockReturnValueOnce(throwError(() => new AttachmentError('ATTACHMENTS_NOT_FOUND', null, 404)));
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.remove(attachment.id);
    expect(store.attachments()).toEqual([attachment]);
    expect(store.feedback()).toMatchObject({ titleKey: Keys.FailedRemove, referenceCode: null });
    expect(store.removingId()).toBeNull();
  });

  it('drops a failed picked file locally without calling the server', () => {
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.upload(TEST_TYPE_KEY, [testFile()]);
    adapter.uploads[0]?.error(tooLarge());
    const rowId = store.uploads()[0]?.id ?? '';
    store.remove(rowId);
    expect(store.uploads()).toEqual([]);
    expect(adapter.remove).not.toHaveBeenCalled();
  });

  it('reports a non-attachment failure as the generic error', () => {
    adapter.remove.mockReturnValueOnce(throwError(() => new Error('boom')));
    store.load(TEST_RECORD_TYPE, TEST_RECORD_ID);
    store.remove(attachment.id);
    expect(store.feedback()).toMatchObject({ messageKey: 'errors.INTERNAL_ERROR', referenceCode: null });
    store.dismissFeedback();
    expect(store.feedback()).toBeNull();
  });
});
