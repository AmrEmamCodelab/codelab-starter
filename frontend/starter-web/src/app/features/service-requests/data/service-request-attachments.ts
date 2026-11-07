import { DgaPageFeedbackKind, type DgaPageFeedbackState } from '@codelab/ng-dga';
import { ApiErrorCodes, HttpStatus, apiErrorMessageKey, tryAsId, type ApiError } from '@codelab/ng-security';
import { CoreTranslationKeys } from '../../../core/i18n/core-translation-keys';
import type { TranslationParams } from '../../../core/i18n/translation.service';
import type { AttachableRecordId } from '../../../shared/attachments/attachments.models';
import { RECORDS_TRANSLATED_CODES, type RecordDetail } from '../../../shared/records';

/** The id domain of the Records kernel's attachments id (`PublicIdOfRecordAttachmentIds`, family `attachable-records`). */
const ATTACHABLE_RECORD_ID_DOMAIN = 'attachable-record';

/**
 * Where a service request's files live: the record type and the record id of the attachment routes, exactly as
 * `GET api/records/service-request/{id}` answers them (`attachableRecordType`, `attachmentsRecordId`). The attachments
 * id is NOT the request's own id: it is a PublicId in another family, so `app-record-attachments` only ever gets it
 * from here.
 */
export interface ServiceRequestAttachmentsTarget {
  readonly recordType: string;
  readonly recordId: AttachableRecordId;
}

/**
 * The refusal a page shows when the record answered without a usable attachments id (an API older than the
 * contract): the platform's `INTERNAL_ERROR`, so the message is the generic one in both languages.
 */
export const SERVICE_REQUEST_ATTACHMENTS_UNAVAILABLE: ApiError = Object.freeze({
  status: HttpStatus.serverError,
  code: ApiErrorCodes.internalError,
  fieldErrors: [],
});

/**
 * The attachments target of a record as the generic records endpoint sent it, or null when the answer carries no
 * usable `attachableRecordType` / `attachmentsRecordId` (missing, blank or not shaped like a PublicId). The shared
 * `RecordDetail` type does not declare the two fields, so they are read defensively from the wire object.
 */
export function serviceRequestAttachmentsTargetOf(record: RecordDetail): ServiceRequestAttachmentsTarget | null {
  if (!('attachableRecordType' in record) || !('attachmentsRecordId' in record)) return null;
  const { attachableRecordType, attachmentsRecordId } = record;
  if (typeof attachableRecordType !== 'string' || attachableRecordType.trim() === '') return null;
  if (typeof attachmentsRecordId !== 'string') return null;
  const recordId = tryAsId(ATTACHABLE_RECORD_ID_DOMAIN, attachmentsRecordId);
  return recordId === null ? null : { recordType: attachableRecordType, recordId };
}

/** Translates a key (the app's `TranslationService.translate`). */
export type TranslateFn = (key: string, params?: TranslationParams) => string;

/**
 * The page feedback shown in place of the attachments widget when its id could not be loaded, or null when there is
 * no failure. The message is the refusal's `errors.<CODE>` text; with a reference code, the code is the message and
 * the refusal its bold first line.
 */
export function serviceRequestAttachmentsFeedback(error: ApiError | null, translate: TranslateFn): DgaPageFeedbackState | null {
  if (error === null) return null;
  const message = translate(apiErrorMessageKey(error, RECORDS_TRANSLATED_CODES));
  const referenceCode = error.referenceCode;
  return referenceCode
    ? {
        kind: DgaPageFeedbackKind.Error,
        title: message,
        message: translate(CoreTranslationKeys.ErrorDialog.ReferenceCode, { code: referenceCode }),
      }
    : { kind: DgaPageFeedbackKind.Error, message };
}
