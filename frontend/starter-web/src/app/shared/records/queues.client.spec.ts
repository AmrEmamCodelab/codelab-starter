import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL } from '../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../core/http/http-context';
import { QueuesClient } from './queues.client';
import { QueuesApiPaths } from './records-api';
import { QueueView } from './records.models';
import { TEST_RECORD_ID, TEST_RECORD_TYPE, TEST_USER_ID, recordsHttpTestProviders } from './records.testing';

const api = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('QueuesClient', () => {
  let client: QueuesClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: recordsHttpTestProviders() });
    client = TestBed.inject(QueuesClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds the contract paths, one encoded segment per value', () => {
    expect(QueuesApiPaths.queue('t')).toBe('api/queues/t');
    expect(QueuesApiPaths.claim('t', 'r')).toBe('api/queues/t/r/claim');
    expect(QueuesApiPaths.release('t', 'r')).toBe('api/queues/t/r/release');
    expect(QueuesApiPaths.assign('t', 'r')).toBe('api/queues/t/r/assign');
    expect(QueuesApiPaths.claim('a/b', '?')).toBe('api/queues/a%2Fb/%3F/claim');
  });

  it('lists a queue view with every set filter and nothing for the unset ones', () => {
    client.list(TEST_RECORD_TYPE, {
      view: QueueView.Unassigned,
      status: 2,
      number: null,
      submittedFrom: '2026-11-01',
      submittedTo: '',
      page: 2,
      pageSize: 10,
    }).subscribe();
    const request = http.expectOne((req) => req.url === api(QueuesApiPaths.queue(TEST_RECORD_TYPE)));
    expect(request.request.method).toBe('GET');
    expect(request.request.urlWithParams).toBe(
      `${api(QueuesApiPaths.queue(TEST_RECORD_TYPE))}?view=unassigned&status=2&submittedFrom=2026-11-01&page=2&pageSize=10`);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush({ items: [], page: 2, pageSize: 10, total: 0 });
  });

  it('claims and releases with an empty POST', () => {
    client.claim(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe();
    client.release(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe();
    const claim = http.expectOne(api(QueuesApiPaths.claim(TEST_RECORD_TYPE, TEST_RECORD_ID)));
    const release = http.expectOne(api(QueuesApiPaths.release(TEST_RECORD_TYPE, TEST_RECORD_ID)));
    for (const request of [claim, release]) {
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toBeNull();
      expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
      request.flush(null, { status: 204, statusText: 'No Content' });
    }
  });

  it('assigns the record to a user by PublicId', () => {
    client.assign(TEST_RECORD_TYPE, TEST_RECORD_ID, TEST_USER_ID).subscribe();
    const request = http.expectOne(api(QueuesApiPaths.assign(TEST_RECORD_TYPE, TEST_RECORD_ID)));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ userId: TEST_USER_ID });
    request.flush(null, { status: 204, statusText: 'No Content' });
  });
});
