import { describe, expect, it } from 'vitest';
import {
  SERVICE_REQUEST_TERMINAL_STATUSES,
  ServiceRequestActions,
  ServiceRequestStatus,
  isTerminalServiceRequestStatus,
} from './service-request.models';
import { SERVICE_REQUESTS_PERMISSION_KEYS } from './service-requests-permissions';

describe('service request models', () => {
  it('mirrors the backend status integers', () => {
    expect([
      ServiceRequestStatus.Draft,
      ServiceRequestStatus.Submitted,
      ServiceRequestStatus.UnderReview,
      ServiceRequestStatus.Returned,
      ServiceRequestStatus.Resubmitted,
      ServiceRequestStatus.Approved,
      ServiceRequestStatus.Rejected,
      ServiceRequestStatus.Cancelled,
    ]).toEqual([1, 2, 3, 4, 5, 6, 7, 8]);
  });

  it('treats approved, rejected and cancelled as terminal only', () => {
    expect([...SERVICE_REQUEST_TERMINAL_STATUSES].sort()).toEqual([6, 7, 8]);
    expect(isTerminalServiceRequestStatus(ServiceRequestStatus.Rejected)).toBe(true);
    expect(isTerminalServiceRequestStatus(ServiceRequestStatus.Returned)).toBe(false);
  });

  it('mirrors the backend action and permission keys', () => {
    expect(Object.values(ServiceRequestActions)).toEqual([
      'submit',
      'start-review',
      'return',
      'resubmit',
      'approve',
      'reject',
      'cancel',
    ]);
    expect(Object.values(SERVICE_REQUESTS_PERMISSION_KEYS)).toEqual([
      'service-requests.create',
      'service-requests.view-own',
      'service-requests.queue',
      'service-requests.review',
      'service-requests.decide',
    ]);
  });
});
