// Permission matching is owned by core (core/permissions/permission-match.ts), where permissionGuard() uses it too;
// shared keeps exporting it for *hasPermission and its callers.
export { PermissionMatchMode, holdsPermissions, type PermissionRequirement } from '../../core/permissions/permission-match';
