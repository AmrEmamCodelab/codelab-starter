/**
 * Pure tree logic for the tree-editor pattern: building a tree from dotted keys (`access.roles.view`), tri-state
 * checks, subtree toggles, filtering and selection diffs. The component renders what these return.
 *
 * The selection is a set of SELECTABLE keys: every key given to {@link buildKeyTree} (or every node marked
 * `selectable`). A branch made up only to group keys (`access`, `access.roles`) is never in the selection; its check
 * box shows the state of the keys under it.
 */

/** A node label: a translation key the component translates, or text shown as is (already translated or data). */
export type TreeLabel = { readonly textKey: string } | { readonly text: string };

/** Turns a node key into its label. */
export type TreeLabelResolver = (key: string) => TreeLabel;

export interface TreeNode {
  /** The full dotted key (unique in the tree). */
  readonly key: string;
  readonly label: TreeLabel;
  /** True when the key itself can be selected (a real permission key, not only a group). */
  readonly selectable: boolean;
  readonly children: readonly TreeNode[];
}

/** The check state of a node. Integers, like every enum the starter owns. */
export enum TreeCheckState {
  Unchecked = 1,
  Checked = 2,
  /** Some, not all, of the keys under the node are selected (`aria-checked="mixed"`). */
  Mixed = 3,
}

/** What changed between two selections. */
export interface TreeSelectionChange {
  readonly added: ReadonlySet<string>;
  readonly removed: ReadonlySet<string>;
  readonly value: ReadonlySet<string>;
}

/** The separator of dotted keys. */
export const KEY_SEGMENT_SEPARATOR = '.';

/** The default label: the key's last segment, as text. */
export function lastSegmentLabel(key: string): TreeLabel {
  const segments = key.split(KEY_SEGMENT_SEPARATOR);
  return { text: segments[segments.length - 1] };
}

interface MutableNode {
  key: string;
  selectable: boolean;
  children: Map<string, MutableNode>;
}

/**
 * Builds the tree of `keys` by their dotted segments, sorted by key at every level. Every given key is selectable;
 * the prefixes between them become group nodes. Empty segments and duplicate keys are ignored.
 */
export function buildKeyTree(keys: Iterable<string>, labelFor: TreeLabelResolver = lastSegmentLabel): TreeNode[] {
  const root = new Map<string, MutableNode>();
  for (const key of keys) {
    const segments = key.split(KEY_SEGMENT_SEPARATOR);
    if (segments.some((segment) => segment.length === 0)) {
      continue;
    }
    insertPath(root, segments);
  }
  return freeze(root, labelFor);
}

function insertPath(root: Map<string, MutableNode>, segments: readonly string[]): void {
  let level = root;
  let node: MutableNode | undefined;
  for (let depth = 0; depth < segments.length; depth++) {
    const key = segments.slice(0, depth + 1).join(KEY_SEGMENT_SEPARATOR);
    node = level.get(key) ?? { key, selectable: false, children: new Map() };
    level.set(key, node);
    level = node.children;
  }
  if (node) {
    node.selectable = true;
  }
}

function freeze(level: Map<string, MutableNode>, labelFor: TreeLabelResolver): TreeNode[] {
  return [...level.values()]
    .sort((a, b) => compareOrdinal(a.key, b.key))
    .map((node) => ({
      key: node.key,
      label: labelFor(node.key),
      selectable: node.selectable,
      children: freeze(node.children, labelFor),
    }));
}

/** Ordinal (code unit) order, the same in every locale, so the tree never reorders when the language changes. */
function compareOrdinal(a: string, b: string): number {
  if (a === b) {
    return 0;
  }
  return a < b ? -1 : 1;
}

/** Every selectable key in `node`'s subtree (the node's own key first when it is selectable). */
export function selectableKeys(node: TreeNode): string[] {
  const own = node.selectable ? [node.key] : [];
  return [...own, ...node.children.flatMap(selectableKeys)];
}

/** Every selectable key in the forest. */
export function allSelectableKeys(nodes: readonly TreeNode[]): string[] {
  return nodes.flatMap(selectableKeys);
}

/** Every key that has children (what expand-all opens). */
export function branchKeys(nodes: readonly TreeNode[]): string[] {
  return nodes.flatMap((node) => (node.children.length > 0 ? [node.key, ...branchKeys(node.children)] : []));
}

/** The node's tri-state over the selectable keys of its subtree. A subtree with no selectable key is unchecked. */
export function checkState(node: TreeNode, selected: ReadonlySet<string>): TreeCheckState {
  const keys = selectableKeys(node);
  const held = keys.filter((key) => selected.has(key)).length;
  if (held === 0) {
    return TreeCheckState.Unchecked;
  }
  return held === keys.length ? TreeCheckState.Checked : TreeCheckState.Mixed;
}

/** The selection with every selectable key of `node`'s subtree added (`checked`) or removed. */
export function toggleSubtree(node: TreeNode, selected: ReadonlySet<string>, checked: boolean): Set<string> {
  const next = new Set(selected);
  for (const key of selectableKeys(node)) {
    if (checked) {
      next.add(key);
    } else {
      next.delete(key);
    }
  }
  return next;
}

/**
 * The forest pruned to the nodes `matches` accepts, keeping their ancestors (so the path stays visible) and their
 * whole subtree (so a matching group shows what it groups).
 */
export function filterTree(nodes: readonly TreeNode[], matches: (node: TreeNode) => boolean): TreeNode[] {
  return nodes.flatMap((node) => {
    if (matches(node)) {
      return [node];
    }
    const children = filterTree(node.children, matches);
    return children.length > 0 ? [{ ...node, children }] : [];
  });
}

/** Keys in `after` but not `before` (added) and the reverse (removed). */
export function diffSelection(before: ReadonlySet<string>, after: ReadonlySet<string>): TreeSelectionChange {
  return {
    added: new Set([...after].filter((key) => !before.has(key))),
    removed: new Set([...before].filter((key) => !after.has(key))),
    value: after,
  };
}

/** Case- and diacritic-insensitive "contains" for the search box (Arabic and Latin text alike). */
export function normalizeForSearch(text: string): string {
  return text.normalize('NFKD').replace(/\p{M}/gu, '').toLocaleLowerCase().trim();
}
