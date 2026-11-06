import { WIRE_ID_PAYLOAD_LENGTH, WIRE_ID_VERSION, asId } from '@codelab/ng-security';
import type { ServiceRequestCategoryId, ServiceRequestDetails, ServiceRequestId } from './service-request.models';

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
