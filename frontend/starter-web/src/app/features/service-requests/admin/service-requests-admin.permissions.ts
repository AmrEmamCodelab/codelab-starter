import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';

/** The Records module's oversight key (backend `RecordsPermissionKeys`): the admin's read-only view of every record. */
export const RecordsOversightPermissionKeyValue = PermissionKeys.RecordsOversightView;

/** The admin screens' permission keys: one read-only key, no write key (the admin never acts on a request). */
export const ServiceRequestsAdminPermissionKeys = {
  Oversight: RecordsOversightPermissionKeyValue,
} as const;
