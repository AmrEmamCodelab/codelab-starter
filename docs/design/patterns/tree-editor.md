# Pattern: tree editor

**Use it for** choosing a set of keys that form a hierarchy: the permissions of a role, the features of a plan, the
organizations a grant covers. Keys are dotted (`access.roles.manage`); the tree is built from the dots, or passed as
explicit nodes.

**Do not use it for** a flat list of a few options (use `dga-checkbox` or `dga-radio-group`) or for navigation.

| Part | Selector | Code |
| --- | --- | --- |
| Tree editor | `app-tree-editor-pattern` | `src/app/patterns/tree-editor/` |
| Tree helpers | `buildKeyTree`, `checkState`, `toggleSubtree`, `filterTree`, `diffSelection` | `src/app/patterns/tree-editor/key-tree.ts` |

Reference screen: `features/admin/access/roles/role-editor.page.*` (permissions of a role, read-only for virtual
roles). API reference: [patterns/MODULE.md](../../../frontend/starter-web/src/app/patterns/MODULE.md).

## Anatomy

```
Permissions                                         12 selected
[filter ..........]   [Expand all] [Collapse all] [Select all] [Clear all]
▾ [■] access                       group: tri-state (some selected)
    ▾ [✓] roles                    group: all selected
         [✓] view
         [✓] manage
    ▸ [ ] grants                   collapsed group
▾ [ ] dashboard
         [ ] view
```

## Build a screen on it

```html
<app-tree-editor-pattern
  labelKey="admin.access.roles.permissions"
  [keys]="store.catalogKeys()" [labelFor]="permissionLabel" [readOnly]="!canManage()"
  [(value)]="store.selectedKeys" (selectionChange)="store.markDirty($event)" />
<dga-button *hasPermission="PermissionKeys.RolesManage" [disabled]="!store.dirty()" (click)="store.save()">
  {{ 'admin.access.editor.save' | t }}
</dga-button>
```

```ts
// Label each key from the permission catalogue's translations, or with a server-provided description.
readonly permissionLabel: TreeLabelResolver = (key) => ({ textKey: `permissions.${key}` });
```

`value` holds selectable keys only (the given `keys`); a group node (`access`, `access.roles`) only shows the state
of the keys below it. `selectionChange` carries `{ added, removed, value }`, so the screen can save a diff or warn
before removing a sensitive permission.

## Contract

| Input / model / output | Meaning |
| --- | --- |
| `labelKey` (required) | Translation key of the group label (the fieldset legend) |
| `keys` | Dotted keys; the tree is built from them |
| `nodes` | Explicit `TreeNode[]`; wins over `keys` |
| `labelFor` | `TreeLabelResolver`: `{ textKey }` (translated) or `{ text }` (already translated); default: the last key segment |
| `value` (model) | The selected keys, a `ReadonlySet<string>` |
| `readOnly` | Shows the selection without letting it change |
| `searchable` | `false` hides the filter |
| `initiallyExpanded` | `false` starts collapsed |
| `selectionChange` (output) | `{ added, removed, value }` after each user change |

## Rules

- The screen gates saving (`*hasPermission`) and decides read-only; the tree never checks permissions.
- Keys come from typed constants or the server catalogue, never typed by hand in a template.
- Save the whole set or the diff from `selectionChange`; the pattern never calls the API.
- Labels are translation keys (`{ textKey }`) whenever the key has one; `{ text }` is only for server text that is
  already in the user's language.

## Accessibility

A `fieldset` named by its legend; native checkboxes with the native mixed state on tri-state parents; expand
toggles with `aria-expanded` and `aria-controls`; the selected count is announced politely. While the filter is
active every matching branch is expanded and expand/collapse all is disabled; select all and clear all act on the
visible keys only.

## Gotchas

- A key that is both a leaf and a prefix of other keys (`access` and `access.roles`) becomes a group that is also
  selectable; keep catalogue keys either leaves or groups.
- Clearing the filter restores the user's own expansion state.
