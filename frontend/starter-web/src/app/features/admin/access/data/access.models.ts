import type { components } from '../../../../core/api/generated/openapi';

type Schemas = components['schemas'];

/** A role's PublicId (label `roles`). */
export type RoleId = Schemas['PublicIdOfRoleIds'];
/** A user's PublicId as the Access endpoints send it (label `users`, interchangeable with the Users module's id). */
export type AccessUserId = Schemas['PublicIdOfUserIds'];

/** The id domains {@link tryAsId} narrows route parameters into (they name the generated brands). */
export const AccessIdDomains = {
  Role: 'public-id-of-role',
  User: 'public-id-of-user',
} as const;

export type CatalogPermission = Schemas['PermissionResponse'];
export type RoleSummary = Schemas['RoleSummaryResponse'];
export type RoleDetail = Schemas['RoleResponse'];
export type CreateRoleBody = Schemas['CreateRoleCommand'];
export type UpdateRoleBody = Schemas['UpdateRoleCommand'];
export type AccessUser = Schemas['AccessUserResponse'];
export type UserGrants = Schemas['UserGrantsResponse'];
export type GrantUser = Schemas['GrantUserResponse'];
export type AssignedRole = Schemas['AssignedRoleResponse'];
export type DirectGrant = Schemas['DirectGrantResponse'];
export type VirtualRole = Schemas['VirtualRoleResponse'];
export type EffectivePermission = Schemas['EffectivePermissionResponse'];
export type PermissionSource = Schemas['PermissionSourceResponse'];
export type PutUserGrantsBody = Schemas['PutUserGrantsCommand'];
export type RoleAssignmentInput = Schemas['RoleAssignmentInput'];
export type PermissionGrantInput = Schemas['PermissionGrantInput'];

/** How far a held permission reaches; mirrors the backend `PermissionScope` (sent as the integer). */
export enum PermissionScope {
  Global = 1,
  OwnOrganization = 2,
}

/** Where an effective permission comes from; mirrors the backend `PermissionSourceKind`. */
export enum PermissionSourceKind {
  Direct = 1,
  Role = 2,
  VirtualRole = 3,
  BreakGlass = 4,
}

/** Which users a virtual role reaches; mirrors the backend `VirtualRoleKind`. */
export enum VirtualRoleKind {
  AllInternalUsers = 1,
  AllExternalUsers = 2,
  AllAuthenticatedUsers = 3,
}

/** Who a user is to the portal's organization; mirrors the kernel `UserKind`. */
export enum AccessUserKind {
  InternalUser = 1,
  ExternalUser = 2,
}

/**
 * The scope a newly selected role or direct permission starts with. Global mirrors what a role grants without an
 * organization model; the editor can narrow it per row before saving.
 */
export const DEFAULT_GRANT_SCOPE = PermissionScope.Global;

/** The Access error codes the screens branch on or render (`errors.<CODE>`), as the backend `AccessErrorCodes`. */
export const AccessErrorCodes = {
  RoleNotFound: 'ACCESS_ROLE_NOT_FOUND',
  RoleCodeTaken: 'ACCESS_ROLE_CODE_TAKEN',
  RoleNameTaken: 'ACCESS_ROLE_NAME_TAKEN',
  UnknownPermission: 'ACCESS_UNKNOWN_PERMISSION',
  UnknownRole: 'ACCESS_UNKNOWN_ROLE',
  VirtualRoleNotAssignable: 'ACCESS_VIRTUAL_ROLE_NOT_ASSIGNABLE',
  UserNotFound: 'ACCESS_USER_NOT_FOUND',
  BreakGlassNotEditable: 'ACCESS_BREAK_GLASS_NOT_EDITABLE',
  LastManager: 'ACCESS_LAST_MANAGER',
  ScopeExceedsCaller: 'ACCESS_SCOPE_EXCEEDS_CALLER',
} as const;

/** Warning codes a successful write may return in `warnings` (rendered as `errors.<CODE>`). */
export const AccessWarningCodes = {
  OnlyBreakGlassManages: 'ACCESS_ONLY_BREAK_GLASS_MANAGES',
} as const;

/** The two scope choices, in display order. */
export const PERMISSION_SCOPES: readonly PermissionScope[] = [PermissionScope.Global, PermissionScope.OwnOrganization];

/** Narrows a wire integer to a known scope; anything else reads as the narrower OwnOrganization (fails safe). */
export function toPermissionScope(value: number): PermissionScope {
  return value === PermissionScope.Global ? PermissionScope.Global : PermissionScope.OwnOrganization;
}
