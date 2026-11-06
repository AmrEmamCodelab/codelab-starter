import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { SERVICE_REQUESTS_PERMISSION_KEYS } from '../data';

/** The queue (Assignments module) keys the staff screens gate on: the generated `PermissionKeys.Assignments*`. */
export const AssignmentsPermissionKeyValues = {
  /** Claim a record and release one you hold. */
  Work: PermissionKeys.AssignmentsWork,
  /** Hand a record to another user. */
  Assign: PermissionKeys.AssignmentsAssign,
} as const;

/** The staff screens' permission keys, named for their routes and templates. */
export const ServiceRequestsStaffPermissionKeys = {
  /** Opens the work queue and the staff detail (the record type's queue permission). */
  Queue: SERVICE_REQUESTS_PERMISSION_KEYS.Queue,
  /** Start the review (the server puts `start-review` in the allowed actions). */
  Review: SERVICE_REQUESTS_PERMISSION_KEYS.Review,
  /** Approve, reject or return. */
  Decide: SERVICE_REQUESTS_PERMISSION_KEYS.Decide,
  /** Claim and release. */
  Work: AssignmentsPermissionKeyValues.Work,
} as const;
