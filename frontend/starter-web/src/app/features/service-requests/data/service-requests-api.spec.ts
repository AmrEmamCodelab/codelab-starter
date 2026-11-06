import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../core/session/app-storage';
import { MemoryStorage } from '../../../core/session/memory-storage';
import { EMPTY_SERVICE_REQUEST_DRAFT_FIELDS, ServiceRequestDraftStep } from './service-request.models';
import { ServiceRequestApiPaths, ServiceRequestsClient } from './service-requests-api';
import { TEST_CATEGORY_ID as CATEGORY, TEST_SERVICE_REQUEST_ID as ID } from './service-requests.testing';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;
const NO_CONTENT = { status: 204, statusText: 'No Content' };

describe('ServiceRequestsClient', () => {
  let http: HttpTestingController;
  let client: ServiceRequestsClient;

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
    client = TestBed.inject(ServiceRequestsClient);
  });

  afterEach(() => http.verify());

  function expectQuiet(path: string, method: string) {
    const request = http.expectOne(url(path));
    expect(request.request.method).toBe(method);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    return request;
  }

  it('creates a draft and answers its id', () => {
    let answer: unknown;
    client.create().subscribe((created) => (answer = created));
    const request = expectQuiet('api/service-requests', 'POST');
    expect(request.request.body).toEqual({});
    request.flush({ id: ID }, { status: 201, statusText: 'Created' });
    expect(answer).toEqual({ id: ID });
  });

  it('reads a draft', () => {
    let answer: unknown;
    client.getDraft(ID).subscribe((draft) => (answer = draft));
    const draft = { id: ID, ...EMPTY_SERVICE_REQUEST_DRAFT_FIELDS };
    expectQuiet(`api/service-requests/${ID}/draft`, 'GET').flush(draft);
    expect(answer).toEqual(draft);
  });

  it('saves a draft with its fields and the step to validate', () => {
    const fields = { ...EMPTY_SERVICE_REQUEST_DRAFT_FIELDS, categoryId: CATEGORY, title: 'Experience certificate' };
    client.saveDraft(ID, { fields, step: ServiceRequestDraftStep.Request }).subscribe();
    const request = expectQuiet(ServiceRequestApiPaths.draft(ID), 'PUT');
    expect(request.request.body).toEqual({
      categoryId: CATEGORY,
      title: 'Experience certificate',
      description: null,
      contactPhone: null,
      preferredDate: null,
      step: 1,
    });
    request.flush(null, NO_CONTENT);
  });

  it('submits a draft and answers the allocated number', () => {
    let answer: unknown;
    client.submit(ID).subscribe((submitted) => (answer = submitted));
    const request = expectQuiet(`api/service-requests/${ID}/submit`, 'POST');
    expect(request.request.body).toEqual({});
    request.flush({ number: 'SR-2026-001' });
    expect(answer).toEqual({ number: 'SR-2026-001' });
  });

  it('reads the request details', () => {
    client.get(ID).subscribe();
    expectQuiet(`api/service-requests/${ID}`, 'GET').flush({ id: ID });
  });
});
