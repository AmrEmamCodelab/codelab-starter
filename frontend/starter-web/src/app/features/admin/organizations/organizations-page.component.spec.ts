import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { adminTestProviders, allByTestId, byTestId, choose, grant, organization, press, problem, settle } from './admin.testing';
import { OrganizationKind } from './data/organization.models';
import { OrganizationsAdminClient } from './data/organizations-api';
import { OrganizationsPageComponent } from './organizations-page.component';

describe('OrganizationsPageComponent', () => {
  let fixture: ComponentFixture<OrganizationsPageComponent>;
  let host: HTMLElement;
  let client: Record<string, ReturnType<typeof vi.fn>>;

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(OrganizationsPageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  beforeEach(() => {
    client = {
      list: vi.fn().mockReturnValue(of({ items: [organization(), organization({ id: 'org-2' as never, code: 'PARTNER', isActive: false })], total: 2 })),
      create: vi.fn().mockReturnValue(of(organization())),
      update: vi.fn().mockReturnValue(of(organization())),
      deactivate: vi.fn().mockReturnValue(of(undefined)),
      reactivate: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      imports: [OrganizationsPageComponent],
      providers: [...adminTestProviders(), { provide: OrganizationsAdminClient, useValue: client }],
    });
  });

  it('lists the organizations with kind labels and status tags', async () => {
    grant(PermissionKeys.OrganizationsView);
    await render();
    const rows = host.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('HQ');
    expect(rows[0].textContent).toContain('admin.organizations.kinds.internal-organization');
    expect(rows[0].textContent).toContain('admin.common.status.active');
    expect(rows[1].textContent).toContain('admin.common.status.inactive');
  });

  it('hides every write action from a viewer', async () => {
    grant(PermissionKeys.OrganizationsView);
    await render();
    expect(byTestId(fixture, 'organization-create')).toBeNull();
    expect(allByTestId(fixture, 'organization-edit')).toHaveLength(0);
    expect(allByTestId(fixture, 'organization-status')).toHaveLength(0);
  });

  it('shows the write actions to a manager, live when the key is granted', async () => {
    grant(PermissionKeys.OrganizationsView);
    await render();
    grant(PermissionKeys.OrganizationsView, PermissionKeys.OrganizationsManage);
    fixture.detectChanges();
    expect(byTestId(fixture, 'organization-create')).not.toBeNull();
    expect(allByTestId(fixture, 'organization-edit')).toHaveLength(2);
    const status = allByTestId(fixture, 'organization-status').map((button) => button.textContent?.trim());
    expect(status).toEqual(['admin.common.actions.deactivate', 'admin.common.actions.reactivate']);
  });

  it('filters by kind', async () => {
    grant(PermissionKeys.OrganizationsView);
    await render();
    choose(byTestId(fixture, 'filter-kind'), String(OrganizationKind.ExternalOrganization));
    await settle(fixture);
    expect(client['list']).toHaveBeenLastCalledWith(expect.objectContaining({ kind: OrganizationKind.ExternalOrganization, page: 1 }));
  });

  it('creates an organization through the dialog and shows server field errors', async () => {
    client['create'].mockReturnValue(throwError(() => problem(HttpStatus.badRequest, 'VALIDATION_FAILED', { nameEn: ['REQUIRED'] })));
    grant(PermissionKeys.OrganizationsView, PermissionKeys.OrganizationsManage);
    await render();
    press(byTestId(fixture, 'organization-create'));
    await settle(fixture);
    const dialog = host.querySelector('dialog[open]') as HTMLElement;
    expect(dialog.textContent).toContain('admin.organizations.editor.create-title');
    expect(dialog.querySelector('dga-checkbox')).toBeNull();
    (dialog.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(client['create']).toHaveBeenCalled();
    expect(dialog.textContent).toContain('errors.REQUIRED');
  });

  it('edits with the active toggle', async () => {
    grant(PermissionKeys.OrganizationsView, PermissionKeys.OrganizationsManage);
    await render();
    press(allByTestId(fixture, 'organization-edit')[0]);
    await settle(fixture);
    const dialog = host.querySelector('dialog[open]') as HTMLElement;
    expect(dialog.textContent).toContain('admin.organizations.editor.edit-title');
    expect(dialog.querySelector('dga-checkbox')).not.toBeNull();
  });

  it('asks before deactivating', async () => {
    grant(PermissionKeys.OrganizationsView, PermissionKeys.OrganizationsManage);
    await render();
    press(allByTestId(fixture, 'organization-status')[0]);
    await settle(fixture);
    const dialog = host.querySelector('dialog[open]') as HTMLElement;
    expect(dialog.textContent).toContain('admin.organizations.status.deactivate-text|code=HQ');
    expect(client['deactivate']).not.toHaveBeenCalled();
    (dialog.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(client['deactivate']).toHaveBeenCalledWith('org-1');
  });

  it('shows the error state when the list fails', async () => {
    client['list'].mockReturnValue(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
    grant(PermissionKeys.OrganizationsView);
    await render();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.INTERNAL_ERROR');
  });
});
