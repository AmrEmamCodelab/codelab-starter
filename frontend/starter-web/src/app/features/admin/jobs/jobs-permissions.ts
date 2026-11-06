import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';

/** The screen's permission keys, named for its templates; the values are the generated PermissionKeys. */
export const JobsPermissionKeys = {
  View: PermissionKeys.JobsView,
  Run: PermissionKeys.JobsRun,
} as const;
