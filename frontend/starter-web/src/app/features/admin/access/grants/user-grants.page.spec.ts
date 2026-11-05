import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../../../core/permissions/permission-keys.generated';
import { click, renderAccessPage, settle, texts } from '../access-pages.testing';
import {
  ROLE_REVIEWER_ID,
  USER_ID,
  fails,
  fakeAccessClient,
  problem,
  userGrants,
  type FakeAccessClient,
} from '../access.testing';
import { PermissionScope } from '../data/access.models';
import { UserGrantsPageComponent } from './user-grants.page';

const CONFLICT = 409;
const GRANTS_URL = `/admin/access/users/${USER_ID}/grants`;
const ROUTES = [{ path: 'admin/access/users/:userId/grants', component: UserGrantsPageComponent }];
const MANAGER = [PermissionKeys.AccessGrantsView, PermissionKeys.AccessGrantsManage, PermissionKeys.AccessRolesView];

describe('UserGrantsPageComponent', () => {
  let client: FakeAccessClient;

  beforeEach(() => {
    client = fakeAccessClient();
  });

  function render(permissions: readonly string[] = MANAGER, url = GRANTS_URL) {
    return renderAccessPage({ routes: ROUTES, url, component: UserGrantsPageComponent, client, permissions });
  }

  it('shows the user, the role choices, the automatic roles and the effective permissions with sources', async () => {
    const { host } = await render();
    expect(texts(host, '.grants__user-name')).toEqual(['sara']);
    expect(host.querySelectorAll('.grants__role')).toHaveLength(1);
    expect(host.querySelectorAll('.grants__role dga-radio-group')).toHaveLength(1);
    expect(texts(host, '.grants__virtual-row .grants__label')).toEqual(['Internal users']);
    expect(texts(host, '.grants__source')).toEqual([
      'admin.access.grants.source.role|name=Case reviewer',
      'admin.access.grants.source.virtual-role|name=Internal users',
      'admin.access.grants.source.direct|name=',
    ]);
    expect(texts(host, '.grants__direct-row .grants__label')).toEqual(['See roles.']);
  });

  it('saves the roles and the direct permissions with their scopes', async () => {
    const { harness, page, host } = await render();
    page['store'].setRoleScope(ROLE_REVIEWER_ID, PermissionScope.OwnOrganization);
    page['store'].setDirectSelection(new Set(['access.roles.view', 'dashboard.view']));
    harness.detectChanges();
    expect(host.querySelectorAll('.grants__direct-row')).toHaveLength(2);

    click(host, '.grants__save');
    await settle(harness);
    expect(client.putUserGrants).toHaveBeenCalledWith(USER_ID, {
      roles: [{ roleId: ROLE_REVIEWER_ID, scope: PermissionScope.OwnOrganization }],
      permissions: [
        { key: 'access.roles.view', scope: PermissionScope.OwnOrganization },
        { key: 'dashboard.view', scope: PermissionScope.Global },
      ],
      confirmBreakGlassOnly: false,
    });
    expect(host.querySelector('.grants__saved')).not.toBeNull();
  });

  it('ticks a role from its checkbox', async () => {
    const { harness, page, host } = await render();
    host.querySelector<HTMLInputElement>('.grants__role input[type="checkbox"]')?.click();
    harness.detectChanges();
    expect(page['store'].roleDraft().size).toBe(0);
    expect(page['store'].dirty()).toBe(true);
  });

  it('asks on the self-lockout guard, shows its text and resends confirmed', async () => {
    client.putUserGrants.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_LAST_MANAGER')));
    const { harness, page, host } = await render();
    page['store'].setRoleAssigned(ROLE_REVIEWER_ID, false);
    harness.detectChanges();
    click(host, '.grants__save');
    await settle(harness);

    const dialog = host.querySelector('app-last-manager-dialog dialog');
    expect(dialog?.hasAttribute('open')).toBe(true);
    expect(dialog?.textContent).toContain('errors.ACCESS_LAST_MANAGER');

    client.putUserGrants.mockReturnValueOnce(of(userGrants({ roles: [], warnings: ['ACCESS_ONLY_BREAK_GLASS_MANAGES'] })));
    await page['store'].confirmLastManager();
    harness.detectChanges();
    expect(client.putUserGrants).toHaveBeenLastCalledWith(USER_ID, expect.objectContaining({ confirmBreakGlassOnly: true }));
    expect(host.textContent).toContain('errors.ACCESS_ONLY_BREAK_GLASS_MANAGES');
  });

  it('shows any other refusal as its i18n error', async () => {
    client.putUserGrants.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_UNKNOWN_ROLE')));
    const { harness, page, host } = await render();
    page['store'].setRoleAssigned(ROLE_REVIEWER_ID, false);
    harness.detectChanges();
    click(host, '.grants__save');
    await settle(harness);
    expect(texts(host, '.grants__save-error')).toEqual(['errors.ACCESS_UNKNOWN_ROLE']);
  });

  it('is read-only without access.grants.manage, and loads no roles or catalog without access.roles.view', async () => {
    const { host } = await render([PermissionKeys.AccessGrantsView]);
    expect(host.querySelector('.grants__read-only')).not.toBeNull();
    expect(host.querySelector('.grants__save')).toBeNull();
    expect(host.querySelector<HTMLInputElement>('.grants__role input[type="checkbox"]')?.disabled).toBe(true);
    expect(client.roles).not.toHaveBeenCalled();
    expect(client.permissions).not.toHaveBeenCalled();
  });

  it('never edits the break-glass administrator', async () => {
    client.userGrants.mockReturnValue(of(userGrants({
      user: { ...userGrants().user, isBreakGlass: true },
    })));
    const { host } = await render();
    expect(host.querySelector('.grants__break-glass-notice')).not.toBeNull();
    expect(host.querySelector('.grants__save')).toBeNull();
  });

  it('refuses a route id that is not a PublicId', async () => {
    const { host } = await render(MANAGER, '/admin/access/users/7/grants');
    expect(client.userGrants).not.toHaveBeenCalled();
    expect(texts(host, '.grants__load-error')).toEqual(['errors.ACCESS_USER_NOT_FOUND']);
  });

  it('asks before leaving unsaved edits', async () => {
    const { page } = await render();
    expect(page.canLeave()).toBe(true);
    page['store'].setRoleAssigned(ROLE_REVIEWER_ID, false);
    const answer = page.canLeave() as Promise<boolean>;
    page['leave'].answer(true);
    await expect(answer).resolves.toBe(true);
  });
});
