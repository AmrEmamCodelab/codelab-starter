import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL, provideTestAppConfig } from '../../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../../core/http/http-context';
import { BROWSER_STORAGE } from '../../../core/session/app-storage';
import { MemoryStorage } from '../../../core/session/memory-storage';
import { AdminJobsClient, JOB_RUNS_TAKE } from './admin-jobs.client';
import { catalog, jobRun } from './jobs.testing';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('AdminJobsClient', () => {
  let http: HttpTestingController;
  let client: AdminJobsClient;

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
    client = TestBed.inject(AdminJobsClient);
  });

  afterEach(() => http.verify());

  it('lists the catalog quietly from api/admin/jobs', () => {
    let answer: unknown;
    client.list().subscribe((jobs) => (answer = jobs));
    const request = http.expectOne(url('api/admin/jobs'));
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush(catalog());
    expect(answer).toEqual(catalog());
  });

  it('starts a run with an empty POST under the escaped key and answers the run id', () => {
    let answer: unknown;
    client.run('outbox drain/1').subscribe((accepted) => (answer = accepted));
    const request = http.expectOne(url('api/admin/jobs/outbox%20drain%2F1/runs'));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toBeNull();
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush({ runId: 'run-9' }, { status: 202, statusText: 'Accepted' });
    expect(answer).toEqual({ runId: 'run-9' });
  });

  it('reads the latest runs with take', () => {
    client.runs('outbox-drain').subscribe();
    const request = http.expectOne((candidate) => candidate.url === url('api/admin/jobs/outbox-drain/runs'));
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('take')).toBe(String(JOB_RUNS_TAKE));
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush([jobRun()]);

    client.runs('outbox-drain', 5).subscribe();
    expect(http.expectOne(url('api/admin/jobs/outbox-drain/runs?take=5')).request.method).toBe('GET');
  });
});
