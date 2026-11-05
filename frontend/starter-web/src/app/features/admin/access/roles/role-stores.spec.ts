import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import {
  ROLE_AUDITOR_ID,
  ROLE_REVIEWER_ID,
  ROLE_SUMMARIES,
  accessTestProviders,
  fails,
  fakeAccessClient,
  fakePermissionSync,
  problem,
  roleDetail,
  type FakeAccessClient,
  type FakePermissionSync,
} from '../access.testing';
import { RoleEditorStore } from './role-editor.store';
import { RoleListStore } from './role-list.store';

const CONFLICT = 409;
const BAD_REQUEST = 400;

describe('RoleListStore', () => {
  let client: FakeAccessClient;
  let sync: FakePermissionSync;
  let store: RoleListStore;

  beforeEach(async () => {
    client = fakeAccessClient();
    sync = fakePermissionSync();
    TestBed.configureTestingModule({ providers: [...accessTestProviders(client, sync), RoleListStore] });
    store = TestBed.inject(RoleListStore);
    await store.load();
  });

  it('loads the roles and filters them by the search', () => {
    expect(store.rows()).toHaveLength(ROLE_SUMMARIES.length);
    store.search.set('auditor');
    expect(store.rows().map((row) => row.id)).toEqual([ROLE_AUDITOR_ID]);
    expect(store.total()).toBe(1);
  });

  it('keeps the load failure for the list\'s error state', async () => {
    client.roles.mockReturnValue(fails(problem(500, 'INTERNAL_ERROR')));
    await store.load();
    expect(store.loadError()?.code).toBe('INTERNAL_ERROR');
    expect(store.loading()).toBe(false);
  });

  it('creates a role with a trimmed body', async () => {
    const created = await store.create({ code: ' case-reviewer ', nameEn: 'Case reviewer', nameAr: 'مراجع الحالات' });
    expect(created?.id).toBe(ROLE_REVIEWER_ID);
    expect(client.createRole).toHaveBeenCalledWith({
      code: 'case-reviewer', nameEn: 'Case reviewer', nameAr: 'مراجع الحالات', permissions: [],
    });
  });

  it('keeps a create refusal with its field errors', async () => {
    client.createRole.mockReturnValue(fails(problem(BAD_REQUEST, 'VALIDATION_FAILED', { code: ['INVALID_FORMAT'] })));
    expect(await store.create({ code: 'Bad Code', nameEn: 'x', nameAr: 'y' })).toBeNull();
    expect(store.createError()?.fieldErrors).toEqual([{ field: 'code', message: 'INVALID_FORMAT' }]);
    store.clearCreateError();
    expect(store.createError()).toBeNull();
  });

  it('deactivates with the role\'s own names and permissions, then refreshes the viewer\'s keys', async () => {
    await store.setActive(ROLE_SUMMARIES[1], false);
    expect(client.updateRole).toHaveBeenCalledWith(ROLE_REVIEWER_ID, {
      nameEn: 'Case reviewer', nameAr: 'مراجع الحالات', permissions: ['dashboard.view', 'reports.export'],
      isActive: false, confirmBreakGlassOnly: false,
    });
    expect(store.rows().find((row) => row.id === ROLE_REVIEWER_ID)?.isActive).toBe(false);
    expect(sync.refresh).toHaveBeenCalled();
    expect(store.busyRoleId()).toBeNull();
  });

  it('asks before the last manager goes, and resends confirmed', async () => {
    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_LAST_MANAGER')));
    await store.setActive(ROLE_SUMMARIES[1], false);
    expect(store.pendingActivation()).toEqual({ role: ROLE_SUMMARIES[1], isActive: false });
    expect(store.actionError()).toBeNull();

    client.updateRole.mockReturnValueOnce(of(roleDetail({ isActive: false, warnings: ['ACCESS_ONLY_BREAK_GLASS_MANAGES'] })));
    await store.confirmPending();
    expect(client.updateRole).toHaveBeenLastCalledWith(ROLE_REVIEWER_ID, expect.objectContaining({ confirmBreakGlassOnly: true }));
    expect(store.pendingActivation()).toBeNull();
    expect(store.warnings()).toEqual(['ACCESS_ONLY_BREAK_GLASS_MANAGES']);
  });

  it('shows any other refusal, and drops a cancelled confirmation', async () => {
    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_ROLE_NAME_TAKEN')));
    await store.setActive(ROLE_SUMMARIES[1], false);
    expect(store.actionError()?.code).toBe('ACCESS_ROLE_NAME_TAKEN');

    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_LAST_MANAGER')));
    await store.setActive(ROLE_SUMMARIES[1], false);
    store.cancelPending();
    expect(store.pendingActivation()).toBeNull();
    await store.confirmPending();
    expect(client.updateRole).toHaveBeenCalledTimes(2);
  });
});

describe('RoleEditorStore', () => {
  let client: FakeAccessClient;
  let sync: FakePermissionSync;
  let store: RoleEditorStore;

  beforeEach(async () => {
    client = fakeAccessClient();
    sync = fakePermissionSync();
    TestBed.configureTestingModule({ providers: [...accessTestProviders(client, sync), RoleEditorStore] });
    store = TestBed.inject(RoleEditorStore);
    await store.load(ROLE_REVIEWER_ID);
  });

  it('loads the catalog and the role into a clean draft', () => {
    expect(store.split().live).toEqual(['access.roles.manage', 'access.roles.view', 'dashboard.view']);
    expect([...store.selection()]).toEqual(['dashboard.view']);
    expect(store.retiredHeld()).toEqual(['reports.export']);
    expect(store.dirty()).toBe(false);
  });

  it('tracks edits and resets them', () => {
    store.selection.set(new Set(['dashboard.view', 'access.roles.view']));
    store.nameEn.set('Senior reviewer');
    expect(store.dirty()).toBe(true);
    store.reset();
    expect(store.dirty()).toBe(false);
    expect(store.nameEn()).toBe('Case reviewer');
  });

  it('saves the tree selection as the permission set', async () => {
    store.selection.set(new Set(['access.roles.view', 'dashboard.view']));
    client.updateRole.mockReturnValueOnce(of(roleDetail({ permissions: ['access.roles.view', 'dashboard.view'] })));
    expect(await store.save()).toBe(true);
    expect(client.updateRole).toHaveBeenCalledWith(ROLE_REVIEWER_ID, {
      nameEn: 'Case reviewer', nameAr: 'مراجع الحالات', permissions: ['access.roles.view', 'dashboard.view'],
      isActive: true, confirmBreakGlassOnly: false,
    });
    expect(store.saved()).toBe(true);
    expect(store.dirty()).toBe(false);
    expect(sync.refresh).toHaveBeenCalled();
  });

  it('asks on a last-manager refusal and saves confirmed', async () => {
    store.selection.set(new Set());
    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_LAST_MANAGER')));
    expect(await store.save()).toBe(false);
    expect(store.lastManagerPending()).toBe(true);
    expect(store.saveError()).toBeNull();

    client.updateRole.mockReturnValueOnce(of(roleDetail({ permissions: [], warnings: ['ACCESS_ONLY_BREAK_GLASS_MANAGES'] })));
    expect(await store.confirmLastManager()).toBe(true);
    expect(client.updateRole).toHaveBeenLastCalledWith(ROLE_REVIEWER_ID, expect.objectContaining({ confirmBreakGlassOnly: true }));
    expect(store.lastManagerPending()).toBe(false);
    expect(store.warnings()).toEqual(['ACCESS_ONLY_BREAK_GLASS_MANAGES']);
  });

  it('keeps other refusals for the page, and a cancelled confirmation closes', async () => {
    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_ROLE_NAME_TAKEN')));
    store.nameEn.set('Auditor');
    expect(await store.save()).toBe(false);
    expect(store.saveError()?.code).toBe('ACCESS_ROLE_NAME_TAKEN');
    expect(store.dirty()).toBe(true);

    client.updateRole.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_LAST_MANAGER')));
    await store.save();
    store.cancelLastManager();
    expect(store.lastManagerPending()).toBe(false);
  });

  it('keeps a load failure and saves nothing without a role', async () => {
    const fresh = TestBed.runInInjectionContext(() => new RoleEditorStore());
    client.role.mockReturnValueOnce(fails(problem(404, 'ACCESS_ROLE_NOT_FOUND')));
    await fresh.load(ROLE_REVIEWER_ID);
    expect(fresh.loadError()?.code).toBe('ACCESS_ROLE_NOT_FOUND');
    expect(await fresh.save()).toBe(false);
  });
});
