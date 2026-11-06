import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { previewRow, problem } from '../organizations/admin.testing';
import { AccountBinding } from '../users/data/user.models';
import type { ImportBatchId, ImportCommit, ImportPreview } from './data/import.models';
import { UserImportClient } from './data/user-import-api';
import { IMPORT_FILE_MEDIA_TYPE, ImportFileProblem } from './import-file';
import { ImportStep, UserImportStore } from './user-import.store';

const BATCH = 'batch-1' as ImportBatchId;
const PREVIEW: ImportPreview = {
  batchId: BATCH,
  rows: [previewRow(), previewRow({ rowNumber: 3, valid: false, errors: ['USERS_IMPORT_ROW_EMAIL_INVALID'] })],
  validCount: 1,
  invalidCount: '1',
};
const COMMIT: ImportCommit = {
  created: [{ id: 'user-9' as never, userName: 'omar', binding: AccountBinding.Password, activationLink: 'http://x/activate#ticket=t', expiresAt: '2026-11-06T10:00:00Z', activationErrorCode: null, activationDelivery: 1 }],
  rejected: [{ rowNumber: 3, errors: ['USERS_IMPORT_ROW_EMAIL_INVALID'] }],
};

function xlsx(name = 'users.xlsx'): File {
  return new File(['x'], name, { type: IMPORT_FILE_MEDIA_TYPE });
}

describe('UserImportStore', () => {
  let client: { preview: ReturnType<typeof vi.fn>; commit: ReturnType<typeof vi.fn> };
  let store: UserImportStore;

  beforeEach(() => {
    client = { preview: vi.fn().mockReturnValue(of(PREVIEW)), commit: vi.fn().mockReturnValue(of(COMMIT)) };
    TestBed.configureTestingModule({ providers: [UserImportStore, { provide: UserImportClient, useValue: client }] });
    store = TestBed.inject(UserImportStore);
  });

  it('starts at the pick step with nothing to send', () => {
    expect(store.step()).toBe(ImportStep.Pick);
    expect(store.canPreview()).toBe(false);
    expect(store.canCommit()).toBe(false);
  });

  it('refuses a wrong file before anything is uploaded', async () => {
    store.selectFile(new File(['x'], 'users.csv', { type: 'text/csv' }));
    expect(store.fileProblem()).toBe(ImportFileProblem.WrongType);
    expect(store.canPreview()).toBe(false);
    await store.uploadPreview();
    expect(client.preview).not.toHaveBeenCalled();
  });

  it('previews a workbook and allows a commit of its valid rows', async () => {
    const file = xlsx();
    store.selectFile(file);
    await store.uploadPreview();
    expect(client.preview).toHaveBeenCalledWith(file);
    expect(store.step()).toBe(ImportStep.Previewed);
    expect(store.validCount()).toBe(1);
    expect(store.invalidCount()).toBe(1);
    expect(store.previewRows()).toHaveLength(2);
    expect(store.previewTotal()).toBe(2);
    expect(store.canCommit()).toBe(true);
  });

  it('allows no commit when no row is valid', async () => {
    client.preview.mockReturnValue(of({ ...PREVIEW, validCount: 0, invalidCount: 2 }));
    store.selectFile(xlsx());
    await store.uploadPreview();
    expect(store.canCommit()).toBe(false);
    await store.commit();
    expect(client.commit).not.toHaveBeenCalled();
  });

  it('pages the preview rows on the client', async () => {
    const rows = Array.from({ length: 25 }, (_, index) => previewRow({ rowNumber: index + 2 }));
    client.preview.mockReturnValue(of({ ...PREVIEW, rows, validCount: 25, invalidCount: 0 }));
    store.selectFile(xlsx());
    await store.uploadPreview();
    expect(store.previewRows()).toHaveLength(20);
    store.previewPage.set(2);
    expect(store.previewRows().map((row) => row.rowNumber)).toEqual([22, 23, 24, 25, 26]);
  });

  it('keeps an upload refusal with its field errors', async () => {
    client.preview.mockReturnValue(
      throwError(() => problem(HttpStatus.badRequest, 'USERS_IMPORT_COLUMNS_MISSING', { Email: ['REQUIRED'] })),
    );
    store.selectFile(xlsx());
    await store.uploadPreview();
    expect(store.step()).toBe(ImportStep.Pick);
    expect(store.error()?.serverCode).toBe('USERS_IMPORT_COLUMNS_MISSING');
    expect(store.busy()).toBe(false);
  });

  it('commits the batch and shows the result', async () => {
    store.selectFile(xlsx());
    await store.uploadPreview();
    await store.commit();
    expect(client.commit).toHaveBeenCalledWith(BATCH);
    expect(store.step()).toBe(ImportStep.Committed);
    expect(store.result()).toEqual(COMMIT);
    expect(store.canCommit()).toBe(false);
  });

  it('keeps the preview when a commit is refused', async () => {
    client.commit.mockReturnValue(throwError(() => problem(HttpStatus.conflict, 'USERS_IMPORT_BATCH_EXPIRED')));
    store.selectFile(xlsx());
    await store.uploadPreview();
    await store.commit();
    expect(store.step()).toBe(ImportStep.Previewed);
    expect(store.error()?.serverCode).toBe('USERS_IMPORT_BATCH_EXPIRED');
  });

  it('starts over from a new pick', async () => {
    store.selectFile(xlsx());
    await store.uploadPreview();
    store.startOver();
    expect(store.file()).toBeNull();
    expect(store.preview()).toBeNull();
    expect(store.step()).toBe(ImportStep.Pick);
  });
});
