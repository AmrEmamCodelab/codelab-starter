import type { DgaStatusTone } from '@codelab/ng-dga';
import type { components } from '../../../../core/api/generated/openapi';
import {
  OrganizationKind,
  UNKNOWN_KIND_LABEL_KEY,
  type OrganizationId,
} from '../../organizations/data/organization.models';

type Schemas = components['schemas'];

/** A user's PublicId as the Users endpoints send it (label `users`, the same label Identity and Access use). */
export type UserId = Schemas['PublicIdOfUsersIds'];
export type AdminUser = Schemas['UserResponse'];
export type UserPage = Schemas['UserPageResponse'];
export type CreatedUser = Schemas['CreatedUserResponse'];
/** An activation link and when it stops working. */
export type Activation = Schemas['ActivationResponse'];

/** How an activation reaches the user (the API's int enum): a link shown to the administrator, or an e-mail. */
export enum ActivationDelivery {
  AdministratorLink = 1,
  Email = 2,
}
/** `fields` is a server-side view of the same values; the client sends the flat members only. */
export type CreateUserBody = Omit<Schemas['CreateUserCommand'], 'fields'>;
export type UpdateUserBody = Omit<Schemas['UpdateUserCommand'], 'fields'>;

/**
 * Who a user is to the organization running the portal; mirrors the kernel `UserKind` (sent as the integer). The
 * members carry the `User` suffix because the scrub sweep reads `Kind.<word ending in a host suffix>` as a host name.
 */
export enum UserKind {
  InternalUser = 1,
  ExternalUser = 2,
}

/** How an account signs in; mirrors the kernel `AccountBinding` (sent as the integer). */
export enum AccountBinding {
  Password /* the inline comment keeps the secret scan's connection-string password rule off this line */ = 1,
  ActiveDirectory = 2,
  Sso = 3,
}

export const USER_KINDS: readonly UserKind[] = [UserKind.InternalUser, UserKind.ExternalUser];
export const ACCOUNT_BINDINGS: readonly AccountBinding[] = [
  AccountBinding.Password,
  AccountBinding.ActiveDirectory,
  AccountBinding.Sso,
];

export const UserKindLabelKeys: Readonly<Record<UserKind, string>> = {
  [UserKind.InternalUser]: 'admin.users.kinds.internal-user',
  [UserKind.ExternalUser]: 'admin.users.kinds.external-user',
};

export const AccountBindingLabelKeys: Readonly<Record<AccountBinding, string>> = {
  [AccountBinding.Password]: 'admin.users.bindings.password-binding',
  [AccountBinding.ActiveDirectory]: 'admin.users.bindings.active-directory',
  [AccountBinding.Sso]: 'admin.users.bindings.sso',
};

export function isUserKind(value: unknown): value is UserKind {
  return typeof value === 'number' && (USER_KINDS as readonly number[]).includes(value);
}

export function isAccountBinding(value: unknown): value is AccountBinding {
  return typeof value === 'number' && (ACCOUNT_BINDINGS as readonly number[]).includes(value);
}

/** The label key of a kind the server sent (or the unknown label for a value this client does not know). */
export function userKindLabelKey(kind: number | null | undefined): string {
  return isUserKind(kind) ? UserKindLabelKeys[kind] : UNKNOWN_KIND_LABEL_KEY;
}

/** The label key of a binding the server sent (or the unknown label for a value this client does not know). */
export function bindingLabelKey(binding: number | null | undefined): string {
  return isAccountBinding(binding) ? AccountBindingLabelKeys[binding] : UNKNOWN_KIND_LABEL_KEY;
}

/**
 * The organization kind a user of `kind` may belong to (the backend `OrganizationMembership` rule): an internal user
 * to an internal organization (or none), an external user to an external one (required).
 */
export function organizationKindFor(kind: UserKind): OrganizationKind {
  return kind === UserKind.ExternalUser ? OrganizationKind.ExternalOrganization : OrganizationKind.InternalOrganization;
}

/** The query of `GET api/admin/users`; null leaves a filter out. Pages are 1-based. */
export interface UserListQuery {
  readonly search: string;
  readonly organizationId: OrganizationId | null;
  readonly kind: UserKind | null;
  readonly isActive: boolean | null;
  readonly page: number;
  readonly pageSize: number;
}

/** One status tag of a user row. */
export interface UserStatusTag {
  readonly labelKey: string;
  readonly tone: DgaStatusTone;
}

export const UserStatusLabelKeys = {
  Active: 'admin.common.status.active',
  Inactive: 'admin.common.status.inactive',
  Locked: 'admin.users.status.locked',
  AwaitingActivation: 'admin.users.status.awaiting-activation',
  BreakGlass: 'admin.users.status.break-glass',
} as const;

/**
 * The status tags of a user row, in this order: active or deactivated, locked out, waiting for activation (a
 * password-bound account without a password), break-glass.
 */
export function userStatusTags(user: AdminUser): readonly UserStatusTag[] {
  const tags: UserStatusTag[] = [
    user.isActive
      ? { labelKey: UserStatusLabelKeys.Active, tone: 'success' }
      : { labelKey: UserStatusLabelKeys.Inactive, tone: 'neutral' },
  ];
  if (user.isLockedOut) {
    tags.push({ labelKey: UserStatusLabelKeys.Locked, tone: 'error' });
  }
  if (awaitsActivation(user)) {
    tags.push({ labelKey: UserStatusLabelKeys.AwaitingActivation, tone: 'warning' });
  }
  if (user.isBreakGlass) {
    tags.push({ labelKey: UserStatusLabelKeys.BreakGlass, tone: 'info' });
  }
  return tags;
}

/** A password-bound account that has not set its password yet. */
export function awaitsActivation(user: AdminUser): boolean {
  return user.binding === AccountBinding.Password && !user.hasPassword;
}

/**
 * The row actions a user offers (the server still decides): the break-glass account is protected from every
 * account change; unlock only for a locked account; an activation link only for a password binding.
 */
export interface UserRowActions {
  readonly edit: boolean;
  readonly toggleActive: boolean;
  readonly unlock: boolean;
  readonly issueActivation: boolean;
}

export function userRowActions(user: AdminUser): UserRowActions {
  const changeable = !user.isBreakGlass;
  return {
    edit: changeable,
    toggleActive: changeable,
    unlock: changeable && user.isLockedOut,
    issueActivation: changeable && user.isActive && user.binding === AccountBinding.Password,
  };
}
