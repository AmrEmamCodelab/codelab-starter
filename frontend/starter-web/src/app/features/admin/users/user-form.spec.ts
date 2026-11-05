import { describe, expect, it } from 'vitest';
import { adminUser } from '../organizations/admin.testing';
import { OrganizationKind, type OrganizationId, type OrganizationOption } from '../organizations/data/organization.models';
import { EditorMode } from '../organizations/organization-form';
import { AccountBinding, UserKind } from './data/user.models';
import { UserForm, bindingOptions, organizationChoices, userKindOptions } from './user-form';

const internalOrg: OrganizationOption = { id: 'org-i' as OrganizationId, name: 'Head office', kind: OrganizationKind.InternalOrganization };
const externalOrg: OrganizationOption = { id: 'org-e' as OrganizationId, name: 'Partner', kind: OrganizationKind.ExternalOrganization };

describe('UserForm', () => {
  it('starts as an internal, password-bound account without an organization', () => {
    const form = UserForm.blank();
    expect(form.mode).toBe(EditorMode.Create);
    expect(form.toCreateBody()).toEqual({
      userName: '',
      displayName: '',
      email: null,
      loginName: null,
      upn: null,
      kind: UserKind.InternalUser,
      binding: AccountBinding.Password,
      organizationId: null,
    });
    expect(form.usesDirectory()).toBe(false);
  });

  it('trims text and sends blank optional fields as null', () => {
    const form = UserForm.blank();
    form.userName.set('  sara ');
    form.displayName.set(' Sara Ali ');
    form.email.set('   ');
    form.upn.set(' sara@example.test ');
    expect(form.toCreateBody()).toMatchObject({ userName: 'sara', displayName: 'Sara Ali', email: null, upn: 'sara@example.test' });
  });

  it('sends unknown radio values as null for the server to refuse', () => {
    const form = UserForm.blank();
    form.kind.set('x');
    form.binding.set(9);
    expect(form.toUpdateBody()).toMatchObject({ kind: null, binding: null });
  });

  it('fills an edit from the user, without the user name in the body', () => {
    const user = adminUser({ email: null, loginName: 'DOMAIN-sara', binding: AccountBinding.ActiveDirectory });
    const form = UserForm.of(user);
    expect(form.mode).toBe(EditorMode.Edit);
    expect(form.id).toBe(user.id);
    expect(form.usesDirectory()).toBe(true);
    const body = form.toUpdateBody();
    expect(body).not.toHaveProperty('userName');
    expect(body).toMatchObject({ email: null, loginName: 'DOMAIN-sara', binding: AccountBinding.ActiveDirectory });
  });

  it('offers organizations of the matching kind only', () => {
    const form = UserForm.blank();
    expect(organizationChoices(form, [internalOrg, externalOrg])).toEqual([internalOrg]);
    form.kind.set(UserKind.ExternalUser);
    expect(organizationChoices(form, [internalOrg, externalOrg])).toEqual([externalOrg]);
  });

  it('keeps the organization the account already belongs to, even when it is not offered any more', () => {
    const user = adminUser({ kind: UserKind.ExternalUser, organizationId: 'org-old' as OrganizationId, organizationName: 'Old partner' });
    const form = UserForm.of(user);
    const choices = organizationChoices(form, [externalOrg]);
    expect(choices.map((choice) => choice.id)).toEqual(['org-old', 'org-e']);
    form.kind.set(UserKind.InternalUser);
    expect(organizationChoices(form, [externalOrg])).toEqual([]);
  });

  it('builds the radio options with translated labels', () => {
    const translate = (key: string) => `t:${key}`;
    expect(userKindOptions(translate).map((option) => option.value)).toEqual([UserKind.InternalUser, UserKind.ExternalUser]);
    expect(bindingOptions(translate)[2]).toEqual({ value: AccountBinding.Sso, label: 't:admin.users.bindings.sso' });
  });
});
