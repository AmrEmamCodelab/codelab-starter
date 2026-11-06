import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import type { EnvironmentProviders, Provider } from '@angular/core';
import { WIRE_ID_PAYLOAD_LENGTH, WIRE_ID_VERSION, asId } from '@codelab/ng-security';
import { TEST_APP_CONFIG, provideTestAppConfig } from '../../core/config/app-config.testing';
import { BROWSER_STORAGE } from '../../core/session/app-storage';
import { MemoryStorage } from '../../core/session/memory-storage';
import type { AttachableRecordId, AttachmentId, RecordAttachment, UploadTicket } from './attachments.models';

/** Spec fixtures for the attachment adapter. Not used by production code. */

/** The attachment service root the specs trust (`origins.attachment`). */
export const TEST_ATTACHMENTS_ROOT = 'http://files.test';
export const TEST_UPLOAD_URL = `${TEST_ATTACHMENTS_ROOT}/api/uploads`;
export const TEST_TICKET_HEADER = 'X-STARTER-Upload-Ticket';
export const TEST_RECORD_TYPE = 'attachments-demo';
export const TEST_TYPE_KEY = 'demo-document';

/** A shape-valid wire id (the browser checks shape only). */
function wireToken(fill: string): string {
  return `${WIRE_ID_VERSION}_k1_${fill.repeat(WIRE_ID_PAYLOAD_LENGTH)}`;
}

export const TEST_RECORD_ID: AttachableRecordId = asId('attachable-record', wireToken('R'));
export const TEST_ATTACHMENT_ID: AttachmentId = asId('attachment', wireToken('A'));

export const TEST_TICKET: UploadTicket = {
  ticket: 'sealed-ticket',
  ticketHeader: TEST_TICKET_HEADER,
  uploadUrl: TEST_UPLOAD_URL,
  expiresAt: '2030-01-01T00:05:00Z',
  allowedExtensions: ['.pdf', '.png'],
  maxFileCount: 1,
};

export const TEST_ATTACHMENT: RecordAttachment = {
  id: TEST_ATTACHMENT_ID,
  typeKey: TEST_TYPE_KEY,
  fileName: 'report.pdf',
  sizeBytes: 4,
  contentType: 'application/pdf',
  uploadedAt: '2030-01-01T00:00:00Z',
};

/** The test app config with the attachment root trusted, plus the HTTP testing backend and memory storage. */
export function provideAttachmentsTesting(): (Provider | EnvironmentProviders)[] {
  return [
    provideTestAppConfig({ ...TEST_APP_CONFIG, origins: { ...TEST_APP_CONFIG.origins, attachment: [TEST_ATTACHMENTS_ROOT] } }),
    provideHttpClient(),
    provideHttpClientTesting(),
    { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
  ];
}

/** A small PDF-named file. */
export function testFile(name = 'report.pdf'): File {
  return new File(['%PDF'], name, { type: 'application/pdf' });
}
