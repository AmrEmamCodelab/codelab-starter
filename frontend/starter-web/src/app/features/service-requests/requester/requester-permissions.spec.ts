import { describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { ServiceRequestsAdminPermissionKeys } from '../admin/service-requests-admin.permissions';
import { SERVICE_REQUESTS_PERMISSION_KEYS } from '../data';
import { AssignmentsPermissionKeyValues, ServiceRequestsStaffPermissionKeys } from '../staff/service-requests-staff.permissions';
import { RequesterGates } from './requester-permissions';

const GENERATED: ReadonlySet<string> = new Set(Object.values(PermissionKeys));

// Regression: the gates once fell back to an empty requirement (never shown) or a cast while the generated catalog
// was stale, so even a real requester was denied. Every sample key must now be a generated catalog member.
describe('service request permission gates', () => {
  it('gates every requester screen on a generated key, never on an empty requirement', () => {
    expect(RequesterGates).toEqual({ Create: PermissionKeys.ServiceRequestsCreate, ViewOwn: PermissionKeys.ServiceRequestsViewOwn });
  });

  it('takes every sample, staff and admin key from the generated catalog', () => {
    const keys = [
      ...Object.values(SERVICE_REQUESTS_PERMISSION_KEYS),
      ...Object.values(ServiceRequestsStaffPermissionKeys),
      ...Object.values(AssignmentsPermissionKeyValues),
      ...Object.values(ServiceRequestsAdminPermissionKeys),
    ];
    for (const key of keys) expect(GENERATED.has(key), key).toBe(true);
  });

  it('maps the staff and admin screens to their backend keys', () => {
    expect(ServiceRequestsStaffPermissionKeys).toEqual({
      Queue: PermissionKeys.ServiceRequestsQueue,
      Review: PermissionKeys.ServiceRequestsReview,
      Decide: PermissionKeys.ServiceRequestsDecide,
      Work: PermissionKeys.AssignmentsWork,
    });
    expect(ServiceRequestsAdminPermissionKeys.Oversight).toBe(PermissionKeys.RecordsOversightView);
  });
});
