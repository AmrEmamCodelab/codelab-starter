import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { PermissionKeys, type PermissionKey } from '../../core/permissions/permission-keys.generated';
import { PermissionMatchMode } from '../../shared/permissions/permission-match';
import { echoTranslate } from '../layout.testing';
import {
  DEFAULT_REQUESTER_SHELL_OPTIONS,
  REQUESTER_SHELL_OPTIONS,
  visibleRequesterNav,
  type RequesterNavItem,
} from './requester-nav';

const OPEN: RequesterNavItem = { id: 'faq', labelKey: 'nav.faq', link: '/portal/faq' };
const GATED: RequesterNavItem = {
  id: 'users',
  labelKey: 'nav.users',
  link: '/admin/users',
  icon: 'user',
  permission: PermissionKeys.UsersView,
};
const BOTH_KEYS: RequesterNavItem = {
  id: 'both',
  labelKey: 'nav.both',
  link: '/both',
  permission: [PermissionKeys.UsersView, PermissionKeys.UsersManage],
  permissionMode: PermissionMatchMode.All,
};
const GROUP: RequesterNavItem = {
  id: 'group',
  labelKey: 'nav.group',
  children: [GATED, { id: 'manage', labelKey: 'nav.manage', link: '/manage', permission: PermissionKeys.UsersManage }],
};

function holding(...keys: PermissionKey[]): (key: string) => boolean {
  const held = new Set<string>(keys);
  return key => held.has(key);
}

describe('visibleRequesterNav', () => {
  it('shows an entry without a requirement to every signed-in user', () => {
    expect(visibleRequesterNav([OPEN], holding(), echoTranslate)).toEqual([
      { id: 'faq', label: 'nav.faq', link: '/portal/faq', icon: undefined },
    ]);
  });

  it('shows a gated entry only to a holder of its key', () => {
    expect(visibleRequesterNav([GATED], holding(), echoTranslate)).toEqual([]);
    expect(visibleRequesterNav([GATED], holding(PermissionKeys.UsersView), echoTranslate)).toEqual([
      { id: 'users', label: 'nav.users', link: '/admin/users', icon: 'user' },
    ]);
  });

  it('honours the match mode', () => {
    expect(visibleRequesterNav([BOTH_KEYS], holding(PermissionKeys.UsersView), echoTranslate)).toEqual([]);
    expect(
      visibleRequesterNav([BOTH_KEYS], holding(PermissionKeys.UsersView, PermissionKeys.UsersManage), echoTranslate),
    ).toHaveLength(1);
  });

  it('keeps a submenu with only its visible children', () => {
    const [group] = visibleRequesterNav([GROUP], holding(PermissionKeys.UsersManage), echoTranslate);
    expect(group?.link).toBeUndefined();
    expect(group?.children?.map(child => child.id)).toEqual(['manage']);
  });

  it('drops a submenu none of whose children is visible', () => {
    expect(visibleRequesterNav([GROUP], holding(), echoTranslate)).toEqual([]);
  });

  it('drops a gated submenu with all its children when its own key is missing', () => {
    const gatedGroup: RequesterNavItem = { ...GROUP, permission: PermissionKeys.AccessAdminView };
    expect(
      visibleRequesterNav([gatedGroup], holding(PermissionKeys.UsersView, PermissionKeys.UsersManage), echoTranslate),
    ).toEqual([]);
  });

  it('drops an entry with neither a link nor children, and an empty permission list (fail closed)', () => {
    const linkless: RequesterNavItem = { id: 'x', labelKey: 'nav.x' };
    const emptyGate: RequesterNavItem = { ...OPEN, permission: [] };
    expect(visibleRequesterNav([linkless, emptyGate], holding(PermissionKeys.UsersView), echoTranslate)).toEqual([]);
  });

  it('keeps the given order', () => {
    const items = visibleRequesterNav([GATED, OPEN], holding(PermissionKeys.UsersView), echoTranslate);
    expect(items.map(item => item.id)).toEqual(['users', 'faq']);
  });
});

describe('REQUESTER_SHELL_OPTIONS', () => {
  it('defaults to no navigation, the app root and no feed page', () => {
    expect(TestBed.inject(REQUESTER_SHELL_OPTIONS)).toBe(DEFAULT_REQUESTER_SHELL_OPTIONS);
    expect(DEFAULT_REQUESTER_SHELL_OPTIONS).toEqual({ navItems: [], homeLink: '/', notificationsLink: null });
  });
});
