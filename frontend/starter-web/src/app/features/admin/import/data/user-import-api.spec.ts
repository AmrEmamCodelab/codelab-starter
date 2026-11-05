import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../../core/session/app-storage';
import { MemoryStorage } from '../../../../core/session/memory-storage';
import type { ImportBatchId } from './import.models';
import { IMPORT_FILE_FIELD, UserImportApiPaths, UserImportClient } from './user-import-api';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('UserImportClient', () => {
  let http: HttpTestingController;
  let client: UserImportClient;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideTestAppConfig(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(UserImportClient);
  });

  afterEach(() => http.verify());

  it('uploads the workbook as multipart field "file", quietly', () => {
    const file = new File(['x'], 'users.xlsx');
    client.preview(file).subscribe();
    const request = http.expectOne(url(UserImportApiPaths.Preview));
    expect(request.request.method).toBe('POST');
    const body = request.request.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect((body.get(IMPORT_FILE_FIELD) as File).name).toBe('users.xlsx');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });

  it('commits a batch by its escaped id with no body', () => {
    client.commit('batch/1' as ImportBatchId).subscribe();
    const request = http.expectOne(url('api/admin/user-import/batch%2F1/commit'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });
});
