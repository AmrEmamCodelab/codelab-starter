import { describe, expect, it } from 'vitest';
import {
  TreeCheckState,
  allSelectableKeys,
  branchKeys,
  buildKeyTree,
  checkState,
  diffSelection,
  filterTree,
  lastSegmentLabel,
  normalizeForSearch,
  selectableKeys,
  toggleSubtree,
  type TreeNode,
} from './key-tree';

const KEYS = ['users.view', 'access.roles.view', 'access.roles.manage', 'access.grants.view', 'users.manage', 'dashboard.view'];

function findNode(nodes: readonly TreeNode[], key: string): TreeNode | null {
  for (const node of nodes) {
    const hit = node.key === key ? node : findNode(node.children, key);
    if (hit) {
      return hit;
    }
  }
  return null;
}

function find(nodes: readonly TreeNode[], key: string): TreeNode {
  const node = findNode(nodes, key);
  expect(node, key).not.toBeNull();
  return node as TreeNode;
}

describe('key tree', () => {
  const tree = buildKeyTree(KEYS);

  it('groups dotted keys by segment, sorted, with only the given keys selectable', () => {
    expect(tree.map((node) => node.key)).toEqual(['access', 'dashboard', 'users']);
    const access = find(tree, 'access');
    expect(access.selectable).toBe(false);
    expect(access.children.map((node) => node.key)).toEqual(['access.grants', 'access.roles']);
    expect(find(tree, 'access.roles').children.map((node) => node.key)).toEqual(['access.roles.manage', 'access.roles.view']);
    expect(find(tree, 'access.roles.view').selectable).toBe(true);
    expect(allSelectableKeys(tree).sort()).toEqual([...KEYS].sort());
  });

  it('labels nodes with the resolver (default: last segment as text)', () => {
    expect(find(tree, 'access.roles.view').label).toEqual({ text: 'view' });
    expect(lastSegmentLabel('single')).toEqual({ text: 'single' });
    const keyed = buildKeyTree(['a.b'], (key) => ({ textKey: `permissions.${key}` }));
    expect(keyed[0].label).toEqual({ textKey: 'permissions.a' });
    expect(keyed[0].children[0].label).toEqual({ textKey: 'permissions.a.b' });
  });

  it('ignores empty segments and duplicates, and keeps a key that is also a group selectable', () => {
    const odd = buildKeyTree(['a..b', '.a', 'a.', 'x', 'x', 'x.y']);
    expect(odd.map((node) => node.key)).toEqual(['x']);
    expect(odd[0].selectable).toBe(true);
    expect(selectableKeys(odd[0])).toEqual(['x', 'x.y']);
  });

  it('lists branch keys for expand-all', () => {
    expect(branchKeys(tree)).toEqual(['access', 'access.grants', 'access.roles', 'dashboard', 'users']);
  });

  it('computes the tri-state of a parent', () => {
    const roles = find(tree, 'access.roles');
    expect(checkState(roles, new Set())).toBe(TreeCheckState.Unchecked);
    expect(checkState(roles, new Set(['access.roles.view']))).toBe(TreeCheckState.Mixed);
    expect(checkState(roles, new Set(['access.roles.view', 'access.roles.manage']))).toBe(TreeCheckState.Checked);
    expect(checkState(find(tree, 'access'), new Set(['access.roles.view', 'access.roles.manage']))).toBe(TreeCheckState.Mixed);
    expect(checkState(find(tree, 'access.roles.view'), new Set(['access.roles.view']))).toBe(TreeCheckState.Checked);
  });

  it('toggles a whole subtree without touching the rest', () => {
    const access = find(tree, 'access');
    const on = toggleSubtree(access, new Set(['users.view']), true);
    expect([...on].sort()).toEqual(['access.grants.view', 'access.roles.manage', 'access.roles.view', 'users.view']);
    const off = toggleSubtree(find(tree, 'access.roles'), on, false);
    expect([...off].sort()).toEqual(['access.grants.view', 'users.view']);
  });

  it('filters keeping ancestors of a match and the subtree of a matching group', () => {
    const byKey = (text: string) => (node: TreeNode) => node.key.includes(text);
    const grants = filterTree(tree, byKey('grants'));
    expect(grants.map((node) => node.key)).toEqual(['access']);
    expect(grants[0].children.map((node) => node.key)).toEqual(['access.grants']);
    expect(grants[0].children[0].children.map((node) => node.key)).toEqual(['access.grants.view']);
    const users = filterTree(tree, (node) => node.key === 'users');
    expect(users[0].children).toHaveLength(2);
    expect(filterTree(tree, () => false)).toEqual([]);
  });

  it('diffs two selections', () => {
    const change = diffSelection(new Set(['a', 'b']), new Set(['b', 'c']));
    expect([...change.added]).toEqual(['c']);
    expect([...change.removed]).toEqual(['a']);
    expect([...change.value]).toEqual(['b', 'c']);
  });

  it('normalizes search text across case and diacritics', () => {
    expect(normalizeForSearch('  Rôles ')).toBe('roles');
    expect(normalizeForSearch('الأدوار')).toBe(normalizeForSearch('الادوار'));
  });

  it('keeps enum values explicit integers', () => {
    expect([TreeCheckState.Unchecked, TreeCheckState.Checked, TreeCheckState.Mixed]).toEqual([1, 2, 3]);
  });
});
