import { WIRE_ID_PAYLOAD_LENGTH, WIRE_ID_VERSION, asId } from '@codelab/ng-security';
import type { AttachableRecordId } from '../../../shared/attachments/attachments.models';
import {
  SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE,
  type ServiceRequestCategoryId,
  type ServiceRequestDetails,
  type ServiceRequestId,
} from './service-request.models';

/** Spec fixtures for the Service Request data layer and its screens. Not used by production code. */

/** A shape-valid wire id (the browser checks shape only). */
function wireToken(fill: string): string {
  return `${WIRE_ID_VERSION}_k1_${fill.repeat(WIRE_ID_PAYLOAD_LENGTH)}`;
}

export const TEST_SERVICE_REQUEST_ID: ServiceRequestId = asId('service-request', wireToken('S'));
export const TEST_CATEGORY_ID: ServiceRequestCategoryId = asId('lookup-items', wireToken('C'));

/** The details of a submitted request, with `overrides` applied. */
export function serviceRequestDetails(overrides: Partial<ServiceRequestDetails> = {}): ServiceRequestDetails {
  return {
    id: TEST_SERVICE_REQUEST_ID,
    number: 'SR-2026-001',
    categoryId: TEST_CATEGORY_ID,
    categoryNameAr: 'شهادات',
    categoryNameEn: 'Certificates',
    title: 'Experience certificate',
    description: 'Please issue an experience certificate.',
    contactPhone: '\u0030512345678',
    preferredDate: '2026-11-20',
    ...overrides,
  };
}

/**
 * The Records-kernel attachments id of the test request (`attachmentsRecordId`, family `attachable-records`). It is
 * deliberately different from `TEST_SERVICE_REQUEST_ID`: a page that passed the request id to the attachments widget
 * fails the specs.
 */
export const TEST_ATTACHMENTS_RECORD_ID: AttachableRecordId = asId('attachable-record', wireToken('A'));

/** The two attachment fields the generic records endpoint adds to a record (`RecordDetailResponse`). */
export interface RecordAttachmentsWireFields {
  readonly attachableRecordType: string;
  readonly attachmentsRecordId: string;
}

/** The attachment fields of the test request, as the server sends them. */
export function recordAttachmentsWire(overrides: Partial<RecordAttachmentsWireFields> = {}): RecordAttachmentsWireFields {
  return { attachableRecordType: SERVICE_REQUEST_ATTACHABLE_RECORD_TYPE, attachmentsRecordId: TEST_ATTACHMENTS_RECORD_ID, ...overrides };
}
