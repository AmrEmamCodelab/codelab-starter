import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';

/** The screen's permission keys, named for its templates; the values are the generated PermissionKeys. */
export const SettingsPermissionKeys = {
  View: PermissionKeys.SettingsView,
  Manage: PermissionKeys.SettingsManage,
} as const;
