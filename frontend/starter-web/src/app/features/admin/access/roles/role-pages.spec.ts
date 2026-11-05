import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../../../core/permissions/permission-keys.generated';
import { StubPageComponent, click, renderAccessPage, settle, texts } from '../access-pages.testing';
import {
  ROLE_INTERNAL_ID,
  ROLE_REVIEWER_ID,
  fails,
  fakeAccessClient,
  problem,
  roleDetail,
  type FakeAccessClient,
} from '../access.testing';
import { VirtualRoleKind } from '../data/access.models';
import { RoleEditorPageComponent } from './role-editor.page';
import { RoleListPageComponent } from './role-list.page';

const CONFLICT = 409;
const ROLES_URL = '/admin/access/roles';
const VIEW_ONLY = [PermissionKeys.AccessRolesView];
const MANAGER = [PermissionKeys.AccessRolesView, PermissionKeys.AccessRolesManage];

function listRoutes() {
  return [
    { path: 'admin/access/roles', component: RoleListPageComponent },
    { path: 'admin/access/roles/:roleId', component: StubPageComponent },
  ];
}

describe('RoleListPageComponent', () => {
  let client: FakeAccessClient;

  beforeEach(() => {
    client = fakeAccessClient();
  });

  it('lists the roles: name, code, automatic or assigned, status and permission count', async () => {
    const { host } = await renderAccessPage({
      routes: listRoutes(), url: ROLES_URL, component: RoleListPageComponent, client, permissions: VIEW_ONLY,
    });
    expect(texts(host, '.role-list__name')).toEqual(['Internal users', 'Case reviewer', 'Auditor']);
    expect(texts(host, '.role-list__code')).toEqual(['all-internal-users', 'case-reviewer', 'auditor']);
    expect(texts(host, '.role-list__virtual')).toEqual(['admin.access.roles.virtual']);
    expect(host.textContent).toContain('admin.access.virtual-kind.all-internal-users');
    expect(host.textContent).toContain('admin.access.roles.inactive');
  });

  it('hides the create and toggle actions without access.roles.manage', async () => {
    const { host } = await renderAccessPage({
      routes: listRoutes(), url: ROLES_URL, component: RoleListPageComponent, client, permissions: VIEW_ONLY,
    });
    expect(host.querySelector('.role-list__new')).toBeNull();
    expect(host.querySelector('.role-list__toggle')).toBeNull();
  });

  it('offers create and deactivate/reactivate to a manager', async () => {
    const { host } = await renderAccessPage({
      routes: listRoutes(), url: ROLES_URL, component: RoleListPageComponent, client, permissions: MANAGER,
    });
    expect(host.querySelector('.role-list__new')).not.toBeNull();
    expect(texts(host, '.role-list__toggle')).toEqual([
      'admin.access.roles.deactivate', 'admin.access.roles.deactivate', 'admin.access.roles.reactivate',
    ]);
  });

  it('deactivates a role from its row', async () => {
    const { harness, host } = await renderAccessPage({
      routes: listRoutes(), url: ROLES_URL, component: RoleListPageComponent, client, permissions: MANAGER,
    });
    host.querySelectorAll<HTMLElement>('.role-list__toggle button')[1].click();
    await settle(harness);
    expect(client.updateRole).toHaveBeenCalledWith(ROLE_REVIEWER_ID, expect.objectContaining({ isActive: false }));
  });

  it('creates a role and opens it in the editor', async () => {
    const { harness, page, host } = await renderAccessPage({
      routes: listRoutes(), url: ROLES_URL, component: RoleListPageComponent, client, permissions: MANAGER,
    });
    click(host, '.role-list__new');
    harness.detectChanges();
    expect(page.createOpen()).toBe(true);
    page.draftCode.set('case-reviewer');
    page.draftNameEn.set('Case reviewer');
    page.draftNameAr.set('مراجع الحالات');
    await page.submitCreate();
    expect(client.createRole).toHaveBeenCalledWith({
      code: 'case-reviewer', nameEn: 'Case reviewer', nameAr: 'مراجع الحالات', permissions: [],
    });
    expect(TestBed.inject(Router).url).toBe(`${ROLES_URL}/${ROLE_REVIEWER_ID}`);
  });

  it('keeps the dialog open on a refusal', async () => {
    client.createRole.mockReturnValue(fails(problem(CONFLICT, 'ACCESS_ROLE_CODE_TAKEN')));
    const { harness, page } = await renderAccessPage({
      routes: listRoutes(), url: ROLES_URL, component: RoleListPageComponent, client, permissions: MANAGER,
    });
    page.openCreate();
    await page.submitCreate();
    harness.detectChanges();
    expect(page.createOpen()).toBe(true);
    expect(TestBed.inject(Router).url).toBe(ROLES_URL);
  });

  it('shows the last-manager question with its error text', async () => {
    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_LAST_MANAGER')));
    const { harness, host } = await renderAccessPage({
      routes: listRoutes(), url: ROLES_URL, component: RoleListPageComponent, client, permissions: MANAGER,
    });
    host.querySelectorAll<HTMLElement>('.role-list__toggle button')[1].click();
    await settle(harness);
    const dialog = host.querySelector('app-last-manager-dialog dialog');
    expect(dialog?.hasAttribute('open')).toBe(true);
    expect(dialog?.textContent).toContain('errors.ACCESS_LAST_MANAGER');
  });
});

describe('RoleEditorPageComponent', () => {
  let client: FakeAccessClient;
  const editorRoutes = [{ path: 'admin/access/roles/:roleId', component: RoleEditorPageComponent }];
  const editorUrl = `${ROLES_URL}/${ROLE_REVIEWER_ID}`;

  beforeEach(() => {
    client = fakeAccessClient();
  });

  it('shows the catalog as a tree with the role\'s keys checked and the retired key apart', async () => {
    const { page, host } = await renderAccessPage({
      routes: editorRoutes, url: editorUrl, component: RoleEditorPageComponent, client, permissions: MANAGER,
    });
    expect(client.role).toHaveBeenCalledWith(ROLE_REVIEWER_ID);
    expect(texts(host, '.role-editor__title')).toEqual(['Case reviewer']);
    expect(host.querySelector('.role-editor__tree')).not.toBeNull();
    expect(host.querySelector('.role-editor__retired')).not.toBeNull();
    expect([...page['store'].selection()]).toEqual(['dashboard.view']);
  });

  it('saves the selection with dirty tracking', async () => {
    const { harness, page, host } = await renderAccessPage({
      routes: editorRoutes, url: editorUrl, component: RoleEditorPageComponent, client, permissions: MANAGER,
    });
    const save = () => host.querySelector<HTMLButtonElement>('.role-editor__save button');
    expect(save()?.disabled).toBe(true);

    page['store'].selection.set(new Set(['dashboard.view', 'access.roles.view']));
    harness.detectChanges();
    expect(save()?.disabled).toBe(false);
    expect(host.querySelector('.role-editor__unsaved')).not.toBeNull();

    client.updateRole.mockReturnValueOnce(of(roleDetail({ permissions: ['access.roles.view', 'dashboard.view'] })));
    save()?.click();
    await settle(harness);
    expect(client.updateRole).toHaveBeenCalledWith(ROLE_REVIEWER_ID, expect.objectContaining({
      permissions: ['access.roles.view', 'dashboard.view'], confirmBreakGlassOnly: false,
    }));
    expect(host.querySelector('.role-editor__saved')).not.toBeNull();
  });

  it('is read-only without access.roles.manage', async () => {
    const { host } = await renderAccessPage({
      routes: editorRoutes, url: editorUrl, component: RoleEditorPageComponent, client, permissions: VIEW_ONLY,
    });
    expect(host.querySelector('.role-editor__read-only')).not.toBeNull();
    expect(host.querySelector('.role-editor__save')).toBeNull();
  });

  it('marks a virtual role as applied automatically', async () => {
    client.role.mockReturnValue(of(roleDetail({
      id: ROLE_INTERNAL_ID, isVirtual: true, virtualKind: VirtualRoleKind.AllInternalUsers, permissions: [],
    })));
    const { host } = await renderAccessPage({
      routes: editorRoutes, url: `${ROLES_URL}/${ROLE_INTERNAL_ID}`, component: RoleEditorPageComponent, client, permissions: MANAGER,
    });
    expect(texts(host, '.role-editor__virtual-notice')[0]).toContain('admin.access.editor.virtual-notice');
    expect(host.querySelector('.role-editor__save')).not.toBeNull();
  });

  it('shows a save refusal with its i18n error', async () => {
    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_ROLE_NAME_TAKEN')));
    const { harness, page, host } = await renderAccessPage({
      routes: editorRoutes, url: editorUrl, component: RoleEditorPageComponent, client, permissions: MANAGER,
    });
    page['store'].nameEn.set('Auditor');
    harness.detectChanges();
    click(host, '.role-editor__save');
    await settle(harness);
    expect(texts(host, '.role-editor__save-error')).toEqual(['errors.ACCESS_ROLE_NAME_TAKEN']);
  });

  it('refuses a route id that is not a PublicId', async () => {
    const { host } = await renderAccessPage({
      routes: editorRoutes, url: `${ROLES_URL}/42`, component: RoleEditorPageComponent, client, permissions: MANAGER,
    });
    expect(client.role).not.toHaveBeenCalled();
    expect(texts(host, '.role-editor__load-error')).toEqual(['errors.ACCESS_ROLE_NOT_FOUND']);
  });

  it('asks before leaving unsaved edits', async () => {
    const { harness, page, host } = await renderAccessPage({
      routes: editorRoutes, url: editorUrl, component: RoleEditorPageComponent, client, permissions: MANAGER,
    });
    expect(page.canLeave()).toBe(true);

    page['store'].nameAr.set('آخر');
    const answer = page.canLeave() as Promise<boolean>;
    harness.detectChanges();
    expect(host.querySelector('app-leave-dialog dialog')?.hasAttribute('open')).toBe(true);
    page['leave'].answer(false);
    await expect(answer).resolves.toBe(false);

    const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
    page.onBeforeUnload(event);
    expect(event.defaultPrevented).toBe(true);
  });
});
