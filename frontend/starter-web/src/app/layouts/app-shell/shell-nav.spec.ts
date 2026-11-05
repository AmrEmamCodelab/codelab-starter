import { describe, expect, it } from 'vitest';
import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { RoutePaths } from '../../core/routing/route-paths';
import { appLink, visibleNavItems, type ShellNavItem } from './shell-nav';

const HOME = ['/', 'admin'];
const USERS = ['/', 'admin', 'users'];

const ITEMS: readonly ShellNavItem[] = [
  {
    path: RoutePaths.Dashboard,
    labelKey: 'layout.nav.dashboard',
    icon: 'dashboard',
    targets: [{ permission: PermissionKeys.DashboardView, link: ['/'] }],
  },
  {
    path: RoutePaths.Admin,
    labelKey: 'layout.nav.admin',
    icon: 'lock',
    targets: [
      { permission: PermissionKeys.AccessAdminView, link: HOME },
      { permission: PermissionKeys.UsersView, link: USERS },
    ],
  },
];

function holding(...keys: PermissionKey[]): (key: PermissionKey) => boolean {
  const held = new Set<string>(keys);
  return key => held.has(key);
}

describe('shell navigation', () => {
  it('offers only the entries whose key the user holds', () => {
    const visible = visibleNavItems(ITEMS, holding(PermissionKeys.DashboardView));
    expect(visible.map(item => item.path)).toEqual([RoutePaths.Dashboard]);
    expect(visible[0]?.link).toEqual(['/']);
  });

  it('offers nothing to a user without keys', () => {
    expect(visibleNavItems(ITEMS, () => false)).toEqual([]);
  });

  it('offers an entry to a holder of any of its target keys, leading to the first target held', () => {
    expect(visibleNavItems(ITEMS, holding(PermissionKeys.UsersView)).map(item => item.link)).toEqual([USERS]);
    expect(visibleNavItems(ITEMS, holding(PermissionKeys.UsersView, PermissionKeys.AccessAdminView)).map(item => item.link)).toEqual([HOME]);
  });

  it('never offers an entry without targets', () => {
    const empty: ShellNavItem = { ...ITEMS[0]!, targets: [] };
    expect(visibleNavItems([empty], () => true)).toEqual([]);
  });

  it('builds absolute links, with the dashboard at the root', () => {
    expect(appLink(RoutePaths.Dashboard)).toEqual(['/']);
    expect(appLink(RoutePaths.Admin)).toEqual(['/', RoutePaths.Admin]);
  });
});
