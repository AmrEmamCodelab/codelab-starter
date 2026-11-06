import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';

/** The screen's permission keys, named for its templates; the values are the generated PermissionKeys. */
export const PortalContentPermissionKeys = {
  View: PermissionKeys.PortalContentView,
  Manage: PermissionKeys.PortalContentManage,
} as const;
