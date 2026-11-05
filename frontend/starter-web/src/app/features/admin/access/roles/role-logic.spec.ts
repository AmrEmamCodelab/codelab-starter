import { describe, expect, it } from 'vitest';
import { CATALOG, ROLE_SUMMARIES, roleDetail } from '../access.testing';
import { draftFromRole, heldRetiredKeys, isRoleDraftDirty, roleUpdateBody, sameKeys, splitCatalog } from './role-draft';
import { activationBody, createRoleBody, filterRoles, matchesRoleSearch, pageOf, permissionCountOf } from './role-list';

describe('role list helpers', () => {
  it('matches the code or either name, ignoring case and spaces', () => {
    const reviewer = ROLE_SUMMARIES[1];
    expect(matchesRoleSearch(reviewer, '  REVIEW ')).toBe(true);
    expect(matchesRoleSearch(reviewer, 'مراجع')).toBe(true);
    expect(matchesRoleSearch(reviewer, 'case-')).toBe(true);
    expect(matchesRoleSearch(reviewer, 'auditor')).toBe(false);
    expect(filterRoles(ROLE_SUMMARIES, '')).toHaveLength(ROLE_SUMMARIES.length);
  });

  it('pages in the browser and clamps a page past the end', () => {
    const rows = [1, 2, 3, 4, 5];
    expect(pageOf(rows, 1, 2)).toEqual([1, 2]);
    expect(pageOf(rows, 3, 2)).toEqual([5]);
    expect(pageOf(rows, 9, 2)).toEqual([5]);
    expect(pageOf([], 1, 2)).toEqual([]);
  });

  it('creates a role with trimmed fields and no permissions', () => {
    expect(createRoleBody({ code: ' case-reviewer ', nameEn: ' Reviewer', nameAr: 'مراجع ' })).toEqual({
      code: 'case-reviewer', nameEn: 'Reviewer', nameAr: 'مراجع', permissions: [],
    });
  });

  it('flips only the active state', () => {
    const role = roleDetail();
    expect(activationBody(role, false, true)).toEqual({
      nameEn: role.nameEn, nameAr: role.nameAr, permissions: role.permissions, isActive: false, confirmBreakGlassOnly: true,
    });
  });

  it('reads the permission count as a number', () => {
    expect(permissionCountOf({ ...ROLE_SUMMARIES[1], permissionCount: '7' })).toBe(7);
  });
});

describe('role draft', () => {
  const split = splitCatalog(CATALOG);

  it('splits the catalog into sorted live keys and retired keys', () => {
    expect(split.live).toEqual(['access.roles.manage', 'access.roles.view', 'dashboard.view']);
    expect([...split.retired]).toEqual(['reports.export']);
  });

  it('keeps only live keys in the draft and lists the retired ones apart', () => {
    const role = roleDetail({ permissions: ['dashboard.view', 'reports.export', 'gone.key'] });
    expect([...draftFromRole(role, split.liveSet).permissions]).toEqual(['dashboard.view']);
    expect(heldRetiredKeys(role, split.liveSet)).toEqual(['gone.key', 'reports.export']);
  });

  it('is clean as loaded, even when the role lists retired keys', () => {
    const role = roleDetail();
    expect(isRoleDraftDirty(role, draftFromRole(role, split.liveSet), split.liveSet)).toBe(false);
  });

  it('is dirty after a name or key change, ignoring surrounding spaces', () => {
    const role = roleDetail();
    const draft = draftFromRole(role, split.liveSet);
    expect(isRoleDraftDirty(role, { ...draft, nameEn: ` ${role.nameEn} ` }, split.liveSet)).toBe(false);
    expect(isRoleDraftDirty(role, { ...draft, nameAr: 'آخر' }, split.liveSet)).toBe(true);
    expect(isRoleDraftDirty(role, { ...draft, permissions: new Set(['access.roles.view']) }, split.liveSet)).toBe(true);
  });

  it('saves the selected live keys sorted, drops retired keys and keeps the active state', () => {
    const role = roleDetail({ isActive: false });
    const body = roleUpdateBody(role, {
      nameEn: ' Reviewer ',
      nameAr: 'مراجع',
      permissions: new Set(['dashboard.view', 'reports.export', 'access.roles.view']),
    }, split.liveSet, false);
    expect(body).toEqual({
      nameEn: 'Reviewer',
      nameAr: 'مراجع',
      permissions: ['access.roles.view', 'dashboard.view'],
      isActive: false,
      confirmBreakGlassOnly: false,
    });
  });

  it('compares key sets', () => {
    expect(sameKeys(new Set(['a', 'b']), new Set(['b', 'a']))).toBe(true);
    expect(sameKeys(new Set(['a']), new Set(['a', 'b']))).toBe(false);
  });
});
