import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { adminTestProviders, adminUser, allByTestId, byTestId, grant, organization, press, settle } from '../organizations/admin.testing';
import { OrganizationsAdminClient } from '../organizations/data/organizations-api';
import { AccountBinding } from './data/user.models';
import { UsersAdminClient } from './data/users-api';
import { UsersPageComponent } from './users-page.component';

const ACTIVATION = { activationLink: 'http://localhost:4280/activate#ticket=abc', expiresAt: '2026-11-06T10:00:00Z', delivery: 1 };

describe('UsersPageComponent', () => {
  let fixture: ComponentFixture<UsersPageComponent>;
  let host: HTMLElement;
  let users: Record<string, ReturnType<typeof vi.fn>>;

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(UsersPageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  const openDialog = () => host.querySelector('dialog[open]') as HTMLElement;

  async function submitDialog(): Promise<void> {
    (openDialog().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
  }

  beforeEach(() => {
    users = {
      list: vi.fn().mockReturnValue(of({
        items: [
          adminUser(),
          adminUser({ id: 'user-2' as never, userName: 'locked', isLockedOut: true, hasPassword: false }),
          adminUser({ id: 'user-3' as never, userName: 'admin', isBreakGlass: true }),
          adminUser({ id: 'user-4' as never, userName: 'directory', binding: AccountBinding.Sso, isActive: false }),
        ],
        total: 4,
      })),
      create: vi.fn().mockReturnValue(of({ user: adminUser({ userName: 'new' }), activation: ACTIVATION })),
      update: vi.fn(),
      deactivate: vi.fn().mockReturnValue(of(undefined)),
      reactivate: vi.fn(),
      unlock: vi.fn().mockReturnValue(of(undefined)),
      issueActivation: vi.fn().mockReturnValue(of(ACTIVATION)),
    };
    TestBed.configureTestingModule({
      imports: [UsersPageComponent],
      providers: [
        ...adminTestProviders(),
        { provide: UsersAdminClient, useValue: users },
        { provide: OrganizationsAdminClient, useValue: { list: vi.fn().mockReturnValue(of({ items: [organization()], total: 1 })) } },
      ],
    });
  });

  it('lists users with kind, binding and status tags', async () => {
    grant(PermissionKeys.UsersView);
    await render();
    const rows = [...host.querySelectorAll('tbody tr')].map((row) => row.textContent ?? '');
    expect(rows).toHaveLength(4);
    expect(rows[0]).toContain('admin.users.kinds.internal-user');
    expect(rows[0]).toContain('admin.users.bindings.password-binding');
    expect(rows[1]).toContain('admin.users.status.locked');
    expect(rows[1]).toContain('admin.users.status.awaiting-activation');
    expect(rows[2]).toContain('admin.users.status.break-glass');
    expect(rows[3]).toContain('admin.common.status.inactive');
  });

  it('shows a viewer no write action, no organization filter and no grants link', async () => {
    grant(PermissionKeys.UsersView);
    await render();
    for (const id of ['user-create', 'user-edit', 'user-status', 'user-unlock', 'user-activation', 'user-grants', 'filter-organization']) {
      expect(allByTestId(fixture, id), id).toHaveLength(0);
    }
  });

  it('offers each manager action only where it applies, and the grants link to a grants viewer', async () => {
    grant(PermissionKeys.UsersView, PermissionKeys.UsersManage, PermissionKeys.AccessGrantsView, PermissionKeys.OrganizationsView);
    await render();
    expect(byTestId(fixture, 'user-create')).not.toBeNull();
    expect(byTestId(fixture, 'filter-organization')).not.toBeNull();
    expect(allByTestId(fixture, 'user-edit')).toHaveLength(3);
    expect(allByTestId(fixture, 'user-unlock')).toHaveLength(1);
    expect(allByTestId(fixture, 'user-activation')).toHaveLength(2);
    const grants = allByTestId(fixture, 'user-grants');
    expect(grants).toHaveLength(4);
    expect(grants[0].getAttribute('href')).toBe('/admin/access/users/user-1/grants');
  });

  it('creates a user and hands over the activation link', async () => {
    grant(PermissionKeys.UsersView, PermissionKeys.UsersManage);
    await render();
    press(byTestId(fixture, 'user-create'));
    await settle(fixture);
    expect(openDialog().textContent).toContain('admin.users.editor.create-title');
    expect(openDialog().textContent).toContain('admin.users.fields.organizations-hidden');
    expect(openDialog().textContent).not.toContain('admin.users.fields.login-name');
    await submitDialog();
    expect(users['create']).toHaveBeenCalled();
    expect(openDialog().textContent).toContain('admin.users.issued.text|userName=new');
    expect((openDialog().querySelector('app-activation-link input') as HTMLInputElement).value).toBe(ACTIVATION.activationLink);
  });

  it('shows the directory fields for a directory binding', async () => {
    grant(PermissionKeys.UsersView, PermissionKeys.UsersManage);
    await render();
    press(allByTestId(fixture, 'user-edit')[0]);
    await settle(fixture);
    const sso = [...openDialog().querySelectorAll<HTMLInputElement>('input[type="radio"]')].find(
      (radio) => radio.value === String(AccountBinding.Sso),
    );
    sso?.click();
    await settle(fixture);
    expect(openDialog().textContent).toContain('admin.users.fields.login-name');
    expect(openDialog().textContent).toContain('admin.users.fields.user-name-fixed|userName=sara');
  });

  it('confirms an unlock before sending it', async () => {
    grant(PermissionKeys.UsersView, PermissionKeys.UsersManage);
    await render();
    press(byTestId(fixture, 'user-unlock'));
    await settle(fixture);
    expect(openDialog().textContent).toContain('admin.users.confirm.unlock-text|userName=locked');
    expect(users['unlock']).not.toHaveBeenCalled();
    await submitDialog();
    expect(users['unlock']).toHaveBeenCalledWith('user-2');
  });

  it('issues a new activation link after confirmation and shows it', async () => {
    grant(PermissionKeys.UsersView, PermissionKeys.UsersManage);
    await render();
    press(allByTestId(fixture, 'user-activation')[0]);
    await settle(fixture);
    expect(openDialog().textContent).toContain('admin.users.confirm.activation-title');
    await submitDialog();
    expect(users['issueActivation']).toHaveBeenCalledWith('user-1');
    expect(openDialog().textContent).toContain('admin.users.issued.title');
  });
});
