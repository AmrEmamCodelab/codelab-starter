import { HttpEventType } from '@angular/common/http';
import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TEST_API_BASE_URL } from '../../core/config/app-config.testing';
import { AUTHORIZATION_HEADER } from '../../core/api/api.service';
import { AttachmentErrorCodes } from './attachment-error-codes';
import { AttachmentError } from './attachment-error';
import { AttachmentsApiPaths, UPLOAD_FILE_PART } from './attachments-api';
import { AttachmentUploadEventKind, type AttachmentUploadEvent } from './attachments.models';
import {
  TEST_ATTACHMENT,
  TEST_ATTACHMENT_ID,
  TEST_RECORD_ID,
  TEST_RECORD_TYPE,
  TEST_TICKET,
  TEST_TICKET_HEADER,
  TEST_TYPE_KEY,
  TEST_UPLOAD_URL,
  provideAttachmentsTesting,
  testFile,
} from './attachments.testing';
import { FileSaver } from './file-saver';
import { RecordAttachmentsAdapter } from './record-attachments.adapter';

const api = (path: string) => `${TEST_API_BASE_URL}/${path}`;
const TICKET_URL = api(AttachmentsApiPaths.uploadTicket(TEST_RECORD_TYPE, TEST_RECORD_ID));
const LINK_URL = api(AttachmentsApiPaths.recordAttachments(TEST_RECORD_TYPE, TEST_RECORD_ID));
const STORED = {
  fileId: 'f1',
  fileName: 'report.pdf',
  sizeBytes: 4,
  contentType: 'application/pdf',
  sha256: 'ab',
  receipt: 'r1.receipt',
};

/** A 32-hex server trace id, assembled at run time. */
const TRACE = 'c0ffee1234' + '5'.repeat(22);

/** Lets the async problem+json decoding settle. */
const settle = () => new Promise((resolve) => setTimeout(resolve));

describe('RecordAttachmentsAdapter', () => {
  let adapter: RecordAttachmentsAdapter;
  let http: HttpTestingController;
  let events: AttachmentUploadEvent[];
  let failure: unknown;
  let completed: boolean;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideAttachmentsTesting() });
    adapter = TestBed.inject(RecordAttachmentsAdapter);
    http = TestBed.inject(HttpTestingController);
    events = [];
    failure = undefined;
    completed = false;
  });

  afterEach(() => http.verify());

  function startUpload(): void {
    adapter.upload(TEST_RECORD_TYPE, TEST_RECORD_ID, TEST_TYPE_KEY, testFile()).subscribe({
      next: (event) => events.push(event),
      error: (error: unknown) => (failure = error),
      complete: () => (completed = true),
    });
  }

  describe('upload', () => {
    it('runs ticket, upload, then link in order and ends with the linked attachment', () => {
      startUpload();

      const ticket = http.expectOne(TICKET_URL);
      expect(ticket.request.method).toBe('POST');
      expect(ticket.request.body).toEqual({ typeKey: TEST_TYPE_KEY });
      http.expectNone(TEST_UPLOAD_URL);
      ticket.flush(TEST_TICKET);

      const upload = http.expectOne(TEST_UPLOAD_URL);
      expect(upload.request.method).toBe('POST');
      expect(upload.request.headers.get(TEST_TICKET_HEADER)).toBe(TEST_TICKET.ticket);
      expect(upload.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
      expect((upload.request.body as FormData).get(UPLOAD_FILE_PART)).toBeInstanceOf(File);
      http.expectNone(LINK_URL);
      upload.flush({ files: [STORED] });

      const link = http.expectOne(LINK_URL);
      expect(link.request.method).toBe('POST');
      expect(link.request.body).toEqual({ typeKey: TEST_TYPE_KEY, receipt: STORED.receipt });
      link.flush(TEST_ATTACHMENT, { status: 201, statusText: 'Created' });

      expect(events).toEqual([{ kind: AttachmentUploadEventKind.Linked, attachment: TEST_ATTACHMENT }]);
      expect(completed).toBe(true);
    });

    it('emits upload progress before the linked attachment', () => {
      startUpload();
      http.expectOne(TICKET_URL).flush(TEST_TICKET);
      const upload = http.expectOne(TEST_UPLOAD_URL);
      upload.event({ type: HttpEventType.UploadProgress, loaded: 1, total: 4 });
      upload.event({ type: HttpEventType.UploadProgress, loaded: 4, total: 4 });
      upload.flush({ files: [STORED] });
      http.expectOne(LINK_URL).flush(TEST_ATTACHMENT);

      expect(events.map((event) => event.kind)).toEqual([
        AttachmentUploadEventKind.Progress,
        AttachmentUploadEventKind.Progress,
        AttachmentUploadEventKind.Linked,
      ]);
      expect(events[0]).toMatchObject({ loadedBytes: 1, totalBytes: 4, percent: 25 });
      expect(events[1]).toMatchObject({ percent: 100 });
    });

    it('stops at a refused ticket: no bytes are sent', async () => {
      startUpload();
      http.expectOne(TICKET_URL).flush(
        { code: AttachmentErrorCodes.TypeNotFound, correlationId: TRACE },
        { status: 404, statusText: 'Not Found' },
      );
      await settle();

      http.expectNone(TEST_UPLOAD_URL);
      expect(failure).toBeInstanceOf(AttachmentError);
      expect(failure).toMatchObject({ code: AttachmentErrorCodes.TypeNotFound, status: 404, referenceCode: 'C0FFE-E1234' });
      expect(events).toEqual([]);
    });

    it("surfaces the service's 413 with its code and reference code, and links nothing", async () => {
      startUpload();
      http.expectOne(TICKET_URL).flush(TEST_TICKET);
      http.expectOne(TEST_UPLOAD_URL).flush(
        {
          type: 'about:blank',
          status: 413,
          code: AttachmentErrorCodes.FileTooLarge,
          correlationId: 'abc',
          referenceCode: 'ATT01-REF42',
        },
        { status: 413, statusText: 'Payload Too Large' },
      );
      await settle();

      http.expectNone(LINK_URL);
      expect(failure).toEqual(new AttachmentError(AttachmentErrorCodes.FileTooLarge, 'ATT01-REF42', 413));
      expect((failure as AttachmentError).referenceCode).toBe('ATT01-REF42');
    });

    it('surfaces a refused link (ATTACHMENTS_RECEIPT_INVALID)', async () => {
      startUpload();
      http.expectOne(TICKET_URL).flush(TEST_TICKET);
      http.expectOne(TEST_UPLOAD_URL).flush({ files: [STORED] });
      http.expectOne(LINK_URL).flush(
        { code: AttachmentErrorCodes.ReceiptInvalid, correlationId: 'abc', referenceCode: 'API01-REF07' },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();

      expect(failure).toMatchObject({ code: AttachmentErrorCodes.ReceiptInvalid, referenceCode: 'API01-REF07', status: 400 });
      expect(completed).toBe(false);
    });

    it('refuses a ticket that names another header, sending no bytes', async () => {
      startUpload();
      http.expectOne(TICKET_URL).flush({ ...TEST_TICKET, ticketHeader: AUTHORIZATION_HEADER });
      await settle();

      http.expectNone(TEST_UPLOAD_URL);
      expect(failure).toMatchObject({ code: 'INTERNAL_ERROR', referenceCode: null, status: 0 });
    });

    it('refuses a ticket whose upload URL is outside the attachment roots', async () => {
      startUpload();
      http.expectOne(TICKET_URL).flush({ ...TEST_TICKET, uploadUrl: 'http://elsewhere.test/api/uploads' });
      await settle();

      http.expectNone(() => true);
      expect(failure).toMatchObject({ code: 'INTERNAL_ERROR', status: 0 });
    });

    it('fails when the service answers without a stored file', async () => {
      startUpload();
      http.expectOne(TICKET_URL).flush(TEST_TICKET);
      http.expectOne(TEST_UPLOAD_URL).flush({ files: [] });
      await settle();

      http.expectNone(LINK_URL);
      expect(failure).toMatchObject({ code: 'INTERNAL_ERROR', status: 200 });
    });
  });

  it('lists the record attachments', () => {
    const list = { items: [TEST_ATTACHMENT], missingRequiredTypeKeys: [] };
    let received: unknown;
    adapter.list(TEST_RECORD_TYPE, TEST_RECORD_ID).subscribe((value) => (received = value));
    const request = http.expectOne(LINK_URL);
    expect(request.request.method).toBe('GET');
    request.flush(list);
    expect(received).toEqual(list);
  });

  it('reads the attachment types of a record type', () => {
    let received: unknown;
    adapter.types(TEST_RECORD_TYPE).subscribe((value) => (received = value));
    const request = http.expectOne((req) => req.url === api(AttachmentsApiPaths.Types));
    expect(request.request.params.get('recordType')).toBe(TEST_RECORD_TYPE);
    request.flush([]);
    expect(received).toEqual([]);
  });

  describe('download', () => {
    it('fetches the bytes and saves them under the file name', () => {
      const save = vi.spyOn(TestBed.inject(FileSaver), 'save').mockImplementation(() => undefined);
      let done = false;
      adapter.download(TEST_ATTACHMENT).subscribe({ complete: () => (done = true) });

      const request = http.expectOne(api(AttachmentsApiPaths.content(TEST_ATTACHMENT_ID)));
      expect(request.request.responseType).toBe('blob');
      const blob = new Blob(['%PDF']);
      request.flush(blob);

      expect(save).toHaveBeenCalledWith(blob, TEST_ATTACHMENT.fileName);
      expect(done).toBe(true);
    });

    it('reads the code out of a problem+json Blob body', async () => {
      const save = vi.spyOn(TestBed.inject(FileSaver), 'save');
      adapter.download(TEST_ATTACHMENT).subscribe({ error: (error: unknown) => (failure = error) });
      const body = new Blob([JSON.stringify({ code: AttachmentErrorCodes.NotFound, correlationId: 'abc', referenceCode: 'API01-REF09' })], {
        type: 'application/problem+json',
      });
      http.expectOne(api(AttachmentsApiPaths.content(TEST_ATTACHMENT_ID))).flush(body, { status: 404, statusText: 'Not Found' });
      await settle();

      expect(save).not.toHaveBeenCalled();
      expect(failure).toMatchObject({ code: AttachmentErrorCodes.NotFound, referenceCode: 'API01-REF09', status: 404 });
    });
  });

  describe('remove', () => {
    it('deletes the attachment', () => {
      let done = false;
      adapter.remove(TEST_ATTACHMENT_ID).subscribe({ complete: () => (done = true) });
      const request = http.expectOne(api(AttachmentsApiPaths.attachment(TEST_ATTACHMENT_ID)));
      expect(request.request.method).toBe('DELETE');
      request.flush(null, { status: 204, statusText: 'No Content' });
      expect(done).toBe(true);
    });

    it('surfaces a refusal as an AttachmentError', async () => {
      adapter.remove(TEST_ATTACHMENT_ID).subscribe({ error: (error: unknown) => (failure = error) });
      http
        .expectOne(api(AttachmentsApiPaths.attachment(TEST_ATTACHMENT_ID)))
        .flush({ code: AttachmentErrorCodes.NotFound }, { status: 404, statusText: 'Not Found' });
      await settle();
      expect(failure).toMatchObject({ code: AttachmentErrorCodes.NotFound, referenceCode: null, status: 404 });
    });
  });
});
