import { DgaPageFeedbackKind } from '@codelab/ng-dga';
import { ApiErrorCodes, HttpStatus, type ApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { CoreTranslationKeys } from '../../../core/i18n/core-translation-keys';
import type { TranslationParams } from '../../../core/i18n/translation.service';
import { RecordsErrorCodes, type RecordDetail } from '../../../shared/records';
import {
  SERVICE_REQUEST_ATTACHMENTS_UNAVAILABLE,
  serviceRequestAttachmentsFeedback,
  serviceRequestAttachmentsTargetOf,
} from './service-request-attachments';
import { SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE, SERVICE_REQUEST_RECORD_TYPE } from './service-request.models';
import {
  TEST_ATTACHMENTS_RECORD_ID,
  TEST_SERVICE_REQUEST_ID,
  recordAttachmentsWire,
  type RecordAttachmentsWireFields,
} from './service-requests.testing';

/** A record as the wire sends it: only the fields the function reads matter, the rest are a minimal shape. */
function wireRecord(fields: Partial<RecordAttachmentsWireFields> | null): RecordDetail {
  const record: RecordDetail = {
    id: TEST_SERVICE_REQUEST_ID,
    recordType: SERVICE_REQUEST_RECORD_TYPE,
    number: null,
    status: 1,
    statusKey: 'draft',
    wording: { requesterKey: 'r', staffKey: 's', adminKey: 'a' },
    isTerminal: false,
    ownerDisplayName: 'Requester One',
    createdAt: '2026-11-01T08:00:00Z',
    submittedAt: null,
    updatedAt: '2026-11-01T08:00:00Z',
    lastReason: null,
    allowedActions: [],
    assignee: null,
    sla: null,
  };
  return fields === null ? record : Object.assign(record, fields);
}

const echo = (key: string, params?: TranslationParams): string => (params ? `${key}${JSON.stringify(params)}` : key);

describe('serviceRequestAttachmentsTargetOf', () => {
  it('answers the server attachments id and type, never the request id', () => {
    const target = serviceRequestAttachmentsTargetOf(wireRecord(recordAttachmentsWire()));

    expect(target).toEqual({ recordType: SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE, recordId: TEST_ATTACHMENTS_RECORD_ID });
    expect(target?.recordId).not.toBe(TEST_SERVICE_REQUEST_ID);
  });

  it('is null when the record carries no attachment fields', () => {
    expect(serviceRequestAttachmentsTargetOf(wireRecord(null))).toBeNull();
  });

  it.each([
    ['a blank record type', { attachableRecordType: '  ' }],
    ['an id that is not a PublicId', { attachmentsRecordId: 'not-a-wire-id' }],
    ['an empty id', { attachmentsRecordId: '' }],
  ])('is null for %s', (_case, overrides) => {
    expect(serviceRequestAttachmentsTargetOf(wireRecord(recordAttachmentsWire(overrides)))).toBeNull();
  });

  it('is null when a field is not a string', () => {
    const record = Object.assign(wireRecord(null), { attachableRecordType: SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE, attachmentsRecordId: 7 });
    expect(serviceRequestAttachmentsTargetOf(record)).toBeNull();
  });
});

describe('serviceRequestAttachmentsFeedback', () => {
  it('is null without a failure', () => {
    expect(serviceRequestAttachmentsFeedback(null, echo)).toBeNull();
  });

  it('shows the translated refusal as an error', () => {
    expect(serviceRequestAttachmentsFeedback(SERVICE_REQUEST_ATTACHMENTS_UNAVAILABLE, echo)).toEqual({
      kind: DgaPageFeedbackKind.Error,
      message: `errors.${ApiErrorCodes.internalError}`,
    });
  });

  it('puts the reference code under the refusal when there is one', () => {
    const error: ApiError = { status: HttpStatus.notFound, code: RecordsErrorCodes.NotFound, fieldErrors: [], referenceCode: 'R7K2-9QX4' };

    expect(serviceRequestAttachmentsFeedback(error, echo)).toEqual({
      kind: DgaPageFeedbackKind.Error,
      title: `errors.${RecordsErrorCodes.NotFound}`,
      message: echo(CoreTranslationKeys.ErrorDialog.ReferenceCode, { code: 'R7K2-9QX4' }),
    });
  });

  it('falls back to the generic message for a code the record screens do not translate', () => {
    const error: ApiError = { status: HttpStatus.conflict, code: 'SOMETHING_ELSE', fieldErrors: [] };
    expect(serviceRequestAttachmentsFeedback(error, echo)?.message).toBe(`errors.${ApiErrorCodes.internalError}`);
  });
});
