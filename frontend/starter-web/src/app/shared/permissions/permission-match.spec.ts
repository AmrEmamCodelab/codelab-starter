import { describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../core/permissions/permission-keys.generated';
import { PermissionMatchMode, holdsPermissions } from './permission-match';

const held = new Set<string>([PermissionKeys.DashboardView]);
const has = (key: string) => held.has(key);

describe('holdsPermissions', () => {
  it('checks one key', () => {
    expect(holdsPermissions(has, PermissionKeys.DashboardView)).toBe(true);
    expect(holdsPermissions(has, PermissionKeys.AccessAdminView)).toBe(false);
  });

  it('matches any key by default, or all of them', () => {
    const both = [PermissionKeys.DashboardView, PermissionKeys.AccessAdminView];
    expect(holdsPermissions(has, both)).toBe(true);
    expect(holdsPermissions(has, both, PermissionMatchMode.Any)).toBe(true);
    expect(holdsPermissions(has, both, PermissionMatchMode.All)).toBe(false);
    expect(holdsPermissions(has, [PermissionKeys.DashboardView], PermissionMatchMode.All)).toBe(true);
  });

  it('fails closed when nothing is named', () => {
    expect(holdsPermissions(() => true, [])).toBe(false);
    expect(holdsPermissions(() => true, [], PermissionMatchMode.All)).toBe(false);
    expect(holdsPermissions(() => true, null)).toBe(false);
    expect(holdsPermissions(() => true, undefined)).toBe(false);
  });

  it('keeps enum values explicit integers', () => {
    expect(PermissionMatchMode.Any).toBe(1);
    expect(PermissionMatchMode.All).toBe(2);
  });
});
