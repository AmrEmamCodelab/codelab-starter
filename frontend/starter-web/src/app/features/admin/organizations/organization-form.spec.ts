import { describe, expect, it } from 'vitest';
import { organization } from './admin.testing';
import { OrganizationKind } from './data/organization.models';
import { EditorMode, OrganizationForm, organizationKindOptions } from './organization-form';

describe('OrganizationForm', () => {
  it('starts blank in create mode', () => {
    const form = OrganizationForm.blank();
    expect(form.mode).toBe(EditorMode.Create);
    expect(form.id).toBeNull();
    expect(form.toBody()).toEqual({ code: '', nameEn: '', nameAr: '', kind: null });
    expect(form.activeChanged()).toBe(false);
  });

  it('fills an edit from the organization and trims the body', () => {
    const form = OrganizationForm.of(organization({ kind: OrganizationKind.ExternalOrganization }));
    expect(form.mode).toBe(EditorMode.Edit);
    form.code.set('  hq-2 ');
    expect(form.toBody()).toEqual({ code: 'hq-2', nameEn: 'Head office', nameAr: 'المقر', kind: OrganizationKind.ExternalOrganization });
  });

  it('sends an unknown kind as null for the server to refuse', () => {
    const form = OrganizationForm.blank();
    form.kind.set('7');
    expect(form.toBody().kind).toBeNull();
  });

  it('notices a changed active state only in edit mode', () => {
    const form = OrganizationForm.of(organization());
    form.isActive.set(false);
    expect(form.activeChanged()).toBe(true);
    const created = OrganizationForm.blank();
    created.isActive.set(false);
    expect(created.activeChanged()).toBe(false);
  });

  it('offers every kind with its translated label', () => {
    expect(organizationKindOptions((key) => `t:${key}`)).toEqual([
      { value: OrganizationKind.InternalOrganization, label: 't:admin.organizations.kinds.internal-organization' },
      { value: OrganizationKind.ExternalOrganization, label: 't:admin.organizations.kinds.external-organization' },
    ]);
  });
});
