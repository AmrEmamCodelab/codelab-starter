import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { beforeEach, describe, expect, it } from 'vitest';
import {
  CATALOG,
  ROLE_AUDITOR_ID,
  ROLE_INTERNAL_ID,
  ROLE_REVIEWER_ID,
  ROLE_SUMMARIES,
  USER_ID,
  accessTestProviders,
  fails,
  fakeAccessClient,
  fakePermissionSync,
  problem,
  userGrants,
  type FakeAccessClient,
  type FakePermissionSync,
} from '../access.testing';
import { PermissionScope, PermissionSourceKind } from '../data/access.models';
import {
  directPermissionKeys,
  draftFromGrants,
  effectiveRows,
  isGrantsDraftDirty,
  putGrantsBody,
  roleOptions,
  withDirectSelection,
  withRole,
  withScope,
} from './grants-draft';
import { UserGrantsStore } from './user-grants.store';

const CONFLICT = 409;

describe('grants draft', () => {
  const grants = userGrants();

  it('reads the saved roles and direct grants with their scopes', () => {
    const draft = draftFromGrants(grants);
    expect([...draft.roles]).toEqual([[ROLE_REVIEWER_ID, PermissionScope.Global]]);
    expect([...draft.permissions]).toEqual([['access.roles.view', PermissionScope.OwnOrganization]]);
    expect(isGrantsDraftDirty(grants, draft)).toBe(false);
  });

  it('is dirty after a scope change and clean again after reverting it', () => {
    const draft = draftFromGrants(grants);
    const narrowed = withScope(draft.roles, ROLE_REVIEWER_ID, PermissionScope.OwnOrganization);
    expect(isGrantsDraftDirty(grants, { ...draft, roles: narrowed })).toBe(true);
    const back = withScope(narrowed, ROLE_REVIEWER_ID, PermissionScope.Global);
    expect(isGrantsDraftDirty(grants, { ...draft, roles: back })).toBe(false);
  });

  it('adds a role at the default scope, keeps an assigned role\'s scope and removes on untick', () => {
    const roles = draftFromGrants(grants).roles;
    expect(withRole(roles, ROLE_AUDITOR_ID, true).get(ROLE_AUDITOR_ID)).toBe(PermissionScope.Global);
    const narrowed = withScope(roles, ROLE_REVIEWER_ID, PermissionScope.OwnOrganization);
    expect(withRole(narrowed, ROLE_REVIEWER_ID, true).get(ROLE_REVIEWER_ID)).toBe(PermissionScope.OwnOrganization);
    expect(withRole(roles, ROLE_REVIEWER_ID, false).has(ROLE_REVIEWER_ID)).toBe(false);
  });

  it('ignores a scope change for an entry that is not selected', () => {
    const roles = draftFromGrants(grants).roles;
    expect(withScope(roles, ROLE_AUDITOR_ID, PermissionScope.OwnOrganization)).toBe(roles);
  });

  it('follows the tree selection, keeping the scope of kept keys', () => {
    const permissions = draftFromGrants(grants).permissions;
    const next = withDirectSelection(permissions, new Set(['dashboard.view', 'access.roles.view']));
    expect([...next]).toEqual([
      ['access.roles.view', PermissionScope.OwnOrganization],
      ['dashboard.view', PermissionScope.Global],
    ]);
    expect(withDirectSelection(permissions, new Set()).size).toBe(0);
  });

  it('builds the PUT payload with every role and direct grant and their scopes, sorted', () => {
    const roles = withRole(draftFromGrants(grants).roles, ROLE_AUDITOR_ID, true, PermissionScope.OwnOrganization);
    const permissions = withDirectSelection(new Map(), new Set(['dashboard.view', 'access.roles.view']), PermissionScope.Global);
    const body = putGrantsBody({ roles, permissions }, false);
    expect(body).toEqual({
      roles: [...[ROLE_AUDITOR_ID, ROLE_REVIEWER_ID]].sort().map((roleId) => ({
        roleId,
        scope: roleId === ROLE_AUDITOR_ID ? PermissionScope.OwnOrganization : PermissionScope.Global,
      })),
      permissions: [
        { key: 'access.roles.view', scope: PermissionScope.Global },
        { key: 'dashboard.view', scope: PermissionScope.Global },
      ],
      confirmBreakGlassOnly: false,
    });
  });

  it('offers active ordinary roles plus the held ones, never a virtual role', () => {
    const held = userGrants({
      roles: [{ ...grants.roles[0], roleId: ROLE_AUDITOR_ID, code: 'auditor', nameEn: 'Auditor', nameAr: 'مدقق', isActive: false }],
    });
    const options = roleOptions(ROLE_SUMMARIES, held);
    expect(options.map((option) => option.roleId)).toEqual([ROLE_AUDITOR_ID, ROLE_REVIEWER_ID]);
    expect(options.find((option) => option.roleId === ROLE_AUDITOR_ID)?.isActive).toBe(false);
    expect(options.some((option) => option.roleId === ROLE_INTERNAL_ID)).toBe(false);
    expect(roleOptions(null, grants).map((option) => option.roleId)).toEqual([ROLE_REVIEWER_ID]);
  });

  it('offers the live catalog, else the built-in keys plus what the user holds', () => {
    expect(directPermissionKeys(['dashboard.view'], ['x.y'], grants)).toEqual(['access.roles.view', 'dashboard.view']);
    expect(directPermissionKeys(null, ['x.y'], grants)).toEqual(['access.roles.view', 'dashboard.view', 'x.y']);
  });

  it('resolves each source to its role names', () => {
    const rows = effectiveRows(grants);
    expect(rows[0].sources.map((source) => [source.kind, source.role?.nameEn ?? null])).toEqual([
      [PermissionSourceKind.Role, 'Case reviewer'],
      [PermissionSourceKind.VirtualRole, 'Internal users'],
    ]);
    expect(rows[1].sources[0].role).toBeNull();
  });
});

describe('UserGrantsStore', () => {
  let client: FakeAccessClient;
  let sync: FakePermissionSync;
  let store: UserGrantsStore;

  beforeEach(async () => {
    client = fakeAccessClient();
    sync = fakePermissionSync();
    TestBed.configureTestingModule({ providers: [...accessTestProviders(client, sync), UserGrantsStore] });
    store = TestBed.inject(UserGrantsStore);
    await store.load(USER_ID, { withRolesAndCatalog: true });
  });

  it('loads the grants, the roles and the catalog', () => {
    expect(client.userGrants).toHaveBeenCalledWith(USER_ID);
    expect(store.roleOptions().map((option) => option.code)).toEqual(['case-reviewer']);
    expect(store.directKeys()).toEqual(['access.roles.manage', 'access.roles.view', 'dashboard.view']);
    expect(store.catalogByKey().size).toBe(CATALOG.length);
    expect(store.dirty()).toBe(false);
  });

  it('loads only the grants without access.roles.view', async () => {
    client.roles.mockClear();
    client.permissions.mockClear();
    await store.load(USER_ID, { withRolesAndCatalog: false });
    expect(client.roles).not.toHaveBeenCalled();
    expect(client.permissions).not.toHaveBeenCalled();
    expect(store.allRoles()).toBeNull();
    expect(store.directKeys()).toContain('users.view');
  });

  it('saves roles and direct permissions with their scopes', async () => {
    store.setRoleScope(ROLE_REVIEWER_ID, PermissionScope.OwnOrganization);
    store.setDirectSelection(new Set(['access.roles.view', 'dashboard.view']));
    store.setDirectScope('dashboard.view', PermissionScope.OwnOrganization);
    expect(store.dirty()).toBe(true);

    expect(await store.save()).toBe(true);
    expect(client.putUserGrants).toHaveBeenCalledWith(USER_ID, {
      roles: [{ roleId: ROLE_REVIEWER_ID, scope: PermissionScope.OwnOrganization }],
      permissions: [
        { key: 'access.roles.view', scope: PermissionScope.OwnOrganization },
        { key: 'dashboard.view', scope: PermissionScope.OwnOrganization },
      ],
      confirmBreakGlassOnly: false,
    });
    expect(store.saved()).toBe(true);
    expect(sync.refresh).toHaveBeenCalled();
  });

  it('unassigns a role and resets the draft', () => {
    store.setRoleAssigned(ROLE_REVIEWER_ID, false);
    expect(store.roleDraft().size).toBe(0);
    store.reset();
    expect(store.roleDraft().size).toBe(1);
  });

  it('asks on the self-lockout refusal and resends confirmed', async () => {
    store.setRoleAssigned(ROLE_REVIEWER_ID, false);
    client.putUserGrants.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_LAST_MANAGER')));
    expect(await store.save()).toBe(false);
    expect(store.lastManagerPending()).toBe(true);

    client.putUserGrants.mockReturnValueOnce(of(userGrants({ roles: [], warnings: ['ACCESS_ONLY_BREAK_GLASS_MANAGES'] })));
    expect(await store.confirmLastManager()).toBe(true);
    expect(client.putUserGrants).toHaveBeenLastCalledWith(USER_ID, expect.objectContaining({ confirmBreakGlassOnly: true }));
    expect(store.warnings()).toEqual(['ACCESS_ONLY_BREAK_GLASS_MANAGES']);
  });

  it('keeps other refusals and a load failure', async () => {
    store.setRoleAssigned(ROLE_REVIEWER_ID, false);
    client.putUserGrants.mockReturnValueOnce(fails(problem(CONFLICT, 'ACCESS_BREAK_GLASS_NOT_EDITABLE')));
    await store.save();
    expect(store.saveError()?.code).toBe('ACCESS_BREAK_GLASS_NOT_EDITABLE');
    store.cancelLastManager();

    client.userGrants.mockReturnValueOnce(fails(problem(404, 'ACCESS_USER_NOT_FOUND')));
    await store.load(USER_ID, { withRolesAndCatalog: true });
    expect(store.loadError()?.code).toBe('ACCESS_USER_NOT_FOUND');
  });

  it('saves nothing before a load', async () => {
    const fresh = TestBed.runInInjectionContext(() => new UserGrantsStore());
    expect(await fresh.save()).toBe(false);
  });
});
