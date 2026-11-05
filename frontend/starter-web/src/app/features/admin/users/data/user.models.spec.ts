import { describe, expect, it } from 'vitest';
import { adminUser } from '../../organizations/admin.testing';
import { OrganizationKind, UNKNOWN_KIND_LABEL_KEY } from '../../organizations/data/organization.models';
import {
  AccountBinding,
  UserKind,
  UserStatusLabelKeys,
  awaitsActivation,
  bindingLabelKey,
  organizationKindFor,
  userKindLabelKey,
  userRowActions,
  userStatusTags,
} from './user.models';

describe('user models', () => {
  it('labels kinds and bindings, with a fallback for unknown values', () => {
    expect(userKindLabelKey(UserKind.ExternalUser)).toBe('admin.users.kinds.external-user');
    expect(bindingLabelKey(AccountBinding.Sso)).toBe('admin.users.bindings.sso');
    expect(userKindLabelKey(null)).toBe(UNKNOWN_KIND_LABEL_KEY);
    expect(bindingLabelKey(42)).toBe(UNKNOWN_KIND_LABEL_KEY);
  });

  it('maps a user kind to the organization kind it may belong to', () => {
    expect(organizationKindFor(UserKind.InternalUser)).toBe(OrganizationKind.InternalOrganization);
    expect(organizationKindFor(UserKind.ExternalUser)).toBe(OrganizationKind.ExternalOrganization);
  });

  it('tags an active user with a password as active only', () => {
    expect(userStatusTags(adminUser())).toEqual([{ labelKey: UserStatusLabelKeys.Active, tone: 'success' }]);
  });

  it('tags deactivated, locked, awaiting activation and break-glass, in that order', () => {
    const user = adminUser({ isActive: false, isLockedOut: true, hasPassword: false, isBreakGlass: true });
    expect(userStatusTags(user).map((tag) => tag.labelKey)).toEqual([
      UserStatusLabelKeys.Inactive,
      UserStatusLabelKeys.Locked,
      UserStatusLabelKeys.AwaitingActivation,
      UserStatusLabelKeys.BreakGlass,
    ]);
  });

  it('never says a directory account awaits activation', () => {
    expect(awaitsActivation(adminUser({ binding: AccountBinding.ActiveDirectory, hasPassword: false }))).toBe(false);
    expect(awaitsActivation(adminUser({ hasPassword: false }))).toBe(true);
  });

  it('offers the row actions that apply', () => {
    expect(userRowActions(adminUser())).toEqual({ edit: true, toggleActive: true, unlock: false, issueActivation: true });
    expect(userRowActions(adminUser({ isLockedOut: true })).unlock).toBe(true);
    expect(userRowActions(adminUser({ binding: AccountBinding.Sso })).issueActivation).toBe(false);
    expect(userRowActions(adminUser({ isActive: false })).issueActivation).toBe(false);
  });

  it('protects the break-glass account from every account action', () => {
    expect(userRowActions(adminUser({ isBreakGlass: true, isLockedOut: true }))).toEqual({
      edit: false,
      toggleActive: false,
      unlock: false,
      issueActivation: false,
    });
  });
});
