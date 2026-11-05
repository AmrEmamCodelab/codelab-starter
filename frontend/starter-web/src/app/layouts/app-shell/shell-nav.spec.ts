import { describe, expect, it } from 'vitest';
import { PermissionKeys } from '../../core/permissions/permission-keys.generated';
import { RoutePaths } from '../../core/routing/route-paths';
import { SHELL_NAV_ITEMS, appLink, visibleNavItems } from './shell-nav';

describe('shell navigation', () => {
  it('gates every entry by a generated permission key', () => {
    const known = new Set<string>(Object.values(PermissionKeys));
    for (const item of SHELL_NAV_ITEMS) {
      expect(known.has(item.permission)).toBe(true);
    }
  });

  it('offers only the entries whose key the user holds', () => {
    const held = new Set<string>([PermissionKeys.DashboardView]);
    const visible = visibleNavItems(SHELL_NAV_ITEMS, key => held.has(key));
    expect(visible.map(item => item.path)).toEqual([RoutePaths.Dashboard]);
  });

  it('offers nothing to a user without keys', () => {
    expect(visibleNavItems(SHELL_NAV_ITEMS, () => false)).toEqual([]);
  });

  it('builds absolute links, with the dashboard at the root', () => {
    expect(appLink(RoutePaths.Dashboard)).toEqual(['/']);
    expect(appLink(RoutePaths.Admin)).toEqual(['/', RoutePaths.Admin]);
  });
});
