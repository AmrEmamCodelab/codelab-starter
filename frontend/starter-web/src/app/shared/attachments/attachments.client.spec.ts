import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TEST_API_BASE_URL } from '../../core/config/app-config.testing';
import { SKIP_ERROR_DIALOG } from '../../core/http/http-context';
import { AttachmentsApiPaths } from './attachments-api';
import { AttachmentsClient } from './attachments.client';
import {
  TEST_ATTACHMENT_ID,
  TEST_RECORD_ID,
  TEST_RECORD_TYPE,
  TEST_TYPE_KEY,
  provideAttachmentsTesting,
} from './attachments.testing';

const api = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('AttachmentsClient', () => {
  let client: AttachmentsClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideAttachmentsTesting() });
    client = TestBed.inject(AttachmentsClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('builds the contract paths, one encoded segment per value', () => {
    expect(AttachmentsApiPaths.Types).toBe('api/attachments/types');
    expect(AttachmentsApiPaths.DemoRecord).toBe('api/attachments/demo-record');
    expect(AttachmentsApiPaths.recordAttachments('t', 'r')).toBe('api/records/t/r/attachments');
    expect(AttachmentsApiPaths.uploadTicket('t', 'r')).toBe('api/records/t/r/attachments/upload-ticket');
    expect(AttachmentsApiPaths.attachment('a')).toBe('api/attachments/a');
    expect(AttachmentsApiPaths.content('a')).toBe('api/attachments/a/content');
    expect(AttachmentsApiPaths.recordAttachments('../x?y', 'a/b')).toBe('api/records/..%2Fx%3Fy/a%2Fb/attachments');
  });

  it('sends every call quiet (the attachment UI shows its own errors)', () => {
    client.types(TEST_RECORD_TYPE).subscribe();
    client.list(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe();
    client.uploadTicket(TEST_RECORD_TYPE, TEST_RECORD_ID, { typeKey: TEST_TYPE_KEY }).subscribe();
    client.content(TEST_ATTACHMENT_ID).subscribe();
    client.remove(TEST_ATTACHMENT_ID).subscribe();
    client.demoRecord().subscribe();

    const requests = http.match(() => true);
    expect(requests).toHaveLength(6);
    for (const request of requests) {
      expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
      request.flush(null);
    }
  });

  it('reads the types of a record type by query', () => {
    client.types(TEST_RECORD_TYPE).subscribe();
    const request = http.expectOne((req) => req.url === api(AttachmentsApiPaths.Types));
    expect(request.request.urlWithParams).toBe(`${api(AttachmentsApiPaths.Types)}?recordType=${TEST_RECORD_TYPE}`);
    request.flush([]);
  });

  it('links a receipt to the record', () => {
    client.link(TEST_RECORD_TYPE, TEST_RECORD_ID, { typeKey: TEST_TYPE_KEY, receipt: 'r1.x' }).subscribe();
    const request = http.expectOne(api(AttachmentsApiPaths.recordAttachments(TEST_RECORD_TYPE, TEST_RECORD_ID)));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ typeKey: TEST_TYPE_KEY, receipt: 'r1.x' });
    request.flush(null);
  });

  it('reads the demo record', () => {
    let received: unknown;
    client.demoRecord().subscribe((value) => (received = value));
    http.expectOne(api(AttachmentsApiPaths.DemoRecord)).flush({ recordType: TEST_RECORD_TYPE, recordId: TEST_RECORD_ID });
    expect(received).toEqual({ recordType: TEST_RECORD_TYPE, recordId: TEST_RECORD_ID });
  });
});
