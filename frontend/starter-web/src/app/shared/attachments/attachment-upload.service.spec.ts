import { HttpTestingController } from '@angular/common/http/testing';
import { HttpEventType } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AUTHORIZATION_HEADER } from '../../core/api/api.service';
import { TokenStore } from '../../core/auth/token.store';
import { SignInMode } from '../../core/auth/sign-in-mode';
import { UNSIGNED_REQUEST } from '../../core/http/http-context';
import { AttachmentErrorCodes } from './attachment-error-codes';
import { AttachmentUploadService, progressOf } from './attachment-upload.service';
import { UPLOAD_FILE_PART } from './attachments-api';
import { AttachmentUploadEventKind, type FileUploadEvent } from './attachments.models';
import { TEST_TICKET, TEST_TICKET_HEADER, TEST_UPLOAD_URL, provideAttachmentsTesting, testFile } from './attachments.testing';

const STORED = { fileId: 'f1', fileName: 'report.pdf', sizeBytes: 4, contentType: 'application/pdf', sha256: 'ab', receipt: 'r1.x' };

describe('AttachmentUploadService', () => {
  let service: AttachmentUploadService;
  let http: HttpTestingController;
  let events: FileUploadEvent[];
  let failure: unknown;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: provideAttachmentsTesting() });
    service = TestBed.inject(AttachmentUploadService);
    http = TestBed.inject(HttpTestingController);
    events = [];
    failure = undefined;
  });

  afterEach(() => http.verify());

  it('sends the file under the contract part name with only the ticket header, even inside a session', () => {
    TestBed.inject(TokenStore).save(
      { accessToken: 'a', refreshToken: 'r', accessTokenExpiresAt: '2030-01-01T00:00:00Z', idleTimeoutMinutes: 15, user: { id: 'u', displayName: 'U' } },
      SignInMode.Basic,
    );
    const file = testFile('scan.png');
    service.upload(TEST_TICKET, file).subscribe((event) => events.push(event));

    const request = http.expectOne(TEST_UPLOAD_URL);
    const sent = (request.request.body as FormData).get(UPLOAD_FILE_PART) as File;
    expect(sent.name).toBe('scan.png');
    expect(request.request.headers.keys()).toEqual([TEST_TICKET_HEADER]);
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    expect(request.request.context.get(UNSIGNED_REQUEST)).toBe(true);

    request.event({ type: HttpEventType.UploadProgress, loaded: 2, total: 4 });
    request.flush({ files: [STORED] });
    expect(events).toEqual([
      { kind: AttachmentUploadEventKind.Progress, loadedBytes: 2, totalBytes: 4, percent: 50 },
      { kind: AttachmentUploadEventKind.Stored, file: STORED },
    ]);
  });

  it('turns a service refusal into an AttachmentError', async () => {
    service.upload(TEST_TICKET, testFile()).subscribe({ error: (error: unknown) => (failure = error) });
    http
      .expectOne(TEST_UPLOAD_URL)
      .flush(
        { code: AttachmentErrorCodes.TicketInvalid, correlationId: 'abc', referenceCode: 'ATT01-REF01' },
        { status: 401, statusText: 'Unauthorized' },
      );
    await new Promise((resolve) => setTimeout(resolve));
    expect(failure).toMatchObject({ code: AttachmentErrorCodes.TicketInvalid, referenceCode: 'ATT01-REF01', status: 401 });
  });

  it.each(['Authorization', 'Cookie', 'X-STARTER-Upload-Ticket\r\nX-Other', 'X--Upload-Ticket'])(
    'refuses a ticket header named %j',
    (ticketHeader) => {
      service.upload({ ...TEST_TICKET, ticketHeader }, testFile()).subscribe({ error: (error: unknown) => (failure = error) });
      expect(failure).toMatchObject({ code: 'INTERNAL_ERROR', status: 0 });
    },
  );
});

describe('progressOf', () => {
  it('reports whole percent, capped at 100', () => {
    expect(progressOf(1, 3).percent).toBe(33);
    expect(progressOf(5, 4).percent).toBe(100);
  });

  it('reports 0 percent and no total when the total is unknown', () => {
    expect(progressOf(10, undefined)).toEqual({ kind: AttachmentUploadEventKind.Progress, loadedBytes: 10, totalBytes: null, percent: 0 });
    expect(progressOf(10, 0).totalBytes).toBeNull();
  });
});
