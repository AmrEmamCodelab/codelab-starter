import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL } from '../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../core/http/http-context';
import { RecordsApiPaths } from './records-api';
import { RecordsClient } from './records.client';
import type { RecordDetail, RecordPage, RecordListItem } from './records.models';
import { TEST_RECORD_ID, TEST_RECORD_TYPE, aRecordDetail, anEntry, recordsHttpTestProviders } from './records.testing';

const api = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('RecordsClient', () => {
  let client: RecordsClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: recordsHttpTestProviders() });
    client = TestBed.inject(RecordsClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds the contract paths, one encoded segment per value', () => {
    expect(RecordsApiPaths.mine('t')).toBe('api/records/t/mine');
    expect(RecordsApiPaths.oversight('t')).toBe('api/records/t/oversight');
    expect(RecordsApiPaths.record('t', 'r')).toBe('api/records/t/r');
    expect(RecordsApiPaths.timeline('t', 'r')).toBe('api/records/t/r/timeline');
    expect(RecordsApiPaths.transitions('t', 'r')).toBe('api/records/t/r/transitions');
    expect(RecordsApiPaths.record('../x?y', 'a/b')).toBe('api/records/..%2Fx%3Fy/a%2Fb');
  });

  it('sends every call quiet (record screens show their own errors)', () => {
    client.mine(TEST_RECORD_TYPE).subscribe();
    client.detail(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe();
    client.timeline(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe();
    client.transition(TEST_RECORD_TYPE, TEST_RECORD_ID, { action: 'approve' }).subscribe();
    client.oversight(TEST_RECORD_TYPE).subscribe();

    const requests = http.match(() => true);
    expect(requests).toHaveLength(5);
    for (const request of requests) {
      expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
      request.flush(null);
    }
  });

  it('reads my records with the status and paging it is given', () => {
    let received: RecordPage<RecordListItem> | undefined;
    client.mine(TEST_RECORD_TYPE, { status: 2, page: 3, pageSize: 20 }).subscribe((page) => (received = page));
    const request = http.expectOne((req) => req.url === api(RecordsApiPaths.mine(TEST_RECORD_TYPE)));
    expect(request.request.method).toBe('GET');
    expect(request.request.urlWithParams).toBe(`${api(RecordsApiPaths.mine(TEST_RECORD_TYPE))}?status=2&page=3&pageSize=20`);
    request.flush({ items: [], page: 3, pageSize: 20, total: 0 });
    expect(received).toEqual({ items: [], page: 3, pageSize: 20, total: 0 });
  });

  it('reads one record and its timeline by PublicId', () => {
    let detail: RecordDetail | undefined;
    client.detail(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe((value) => (detail = value));
    http.expectOne(api(RecordsApiPaths.record(TEST_RECORD_TYPE, TEST_RECORD_ID))).flush(aRecordDetail());
    expect(detail?.id).toBe(TEST_RECORD_ID);

    let entries: unknown;
    client.timeline(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe((value) => (entries = value));
    http.expectOne(api(RecordsApiPaths.timeline(TEST_RECORD_TYPE, TEST_RECORD_ID))).flush([anEntry()]);
    expect(entries).toEqual([anEntry()]);
  });

  it('posts a transition with the trimmed reason and answers the record as it is now', () => {
    let detail: RecordDetail | undefined;
    client.transition(TEST_RECORD_TYPE, TEST_RECORD_ID, { action: 'reject', reason: '  incomplete  ' }).subscribe((value) => (detail = value));
    const request = http.expectOne(api(RecordsApiPaths.transitions(TEST_RECORD_TYPE, TEST_RECORD_ID)));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ action: 'reject', reason: 'incomplete' });
    request.flush(aRecordDetail({ status: 7, isTerminal: true, allowedActions: [] }));
    expect(detail?.isTerminal).toBe(true);
  });

  it.each([null, undefined, '   '])('leaves a blank reason (%s) out of the transition body', (reason) => {
    client.transition(TEST_RECORD_TYPE, TEST_RECORD_ID, { action: 'approve', reason }).subscribe();
    const request = http.expectOne(api(RecordsApiPaths.transitions(TEST_RECORD_TYPE, TEST_RECORD_ID)));
    expect(request.request.body).toEqual({ action: 'approve' });
    request.flush(aRecordDetail());
  });

  it('reads the oversight list with the status and number filters', () => {
    client.oversight(TEST_RECORD_TYPE, { status: 6, number: ' TR-2026 ', page: 1 }).subscribe();
    const request = http.expectOne((req) => req.url === api(RecordsApiPaths.oversight(TEST_RECORD_TYPE)));
    expect(request.request.urlWithParams).toBe(`${api(RecordsApiPaths.oversight(TEST_RECORD_TYPE))}?status=6&number=TR-2026&page=1`);
    request.flush({ items: [], page: 1, pageSize: 20, total: 0 });
  });
});
