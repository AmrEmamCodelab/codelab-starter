# Pattern: admin list + editor dialog

**Use it for** an administration page that lists records and creates or edits them in place: users,
organizations, roles, lookup values, settings. The list is server-paged; create and edit open a modal dialog over it.

**Do not use it for** staff work items (use the [work queue](work-queue.md)) or a requester's own requests.

| Part | Selector | Code |
| --- | --- | --- |
| List | `app-admin-list-pattern` | `src/app/patterns/admin-list/` |
| Pager (inside the list, usable alone) | `app-list-pager` | `src/app/patterns/admin-list/list-pager.component.*` |
| Editor dialog | `app-editor-dialog-pattern` | `src/app/patterns/editor-dialog/` |

Reference screens: `features/admin/users/` (list, filters, create/edit dialog) and `features/admin/organizations/`.
API reference: [patterns/MODULE.md](../../../frontend/starter-web/src/app/patterns/MODULE.md).

## Anatomy

```
┌───────────────────────────────────────────────────────────────┐
│ Title                                   [toolbar: New ...]   │  titleKey + [listToolbar] slot (gated by the screen)
│ [search ......]  [filters slot]                               │  debounced search + [listFilters] slot
├───────────────────────────────────────────────────────────────┤
│ Column ▲ │ Column │ Status          │ Actions                 │  ColumnDef[]; sortable headers (aria-sort)
│ ...      │ ...    │ <dga-status-tag>│ [Edit] [Deactivate]     │  listCell templates, listRowActions template
├───────────────────────────────────────────────────────────────┤
│ 21–40 of 95          ‹ 1 … 2 [3] 4 … 5 ›                      │  pager: 1-based [(page)]
└───────────────────────────────────────────────────────────────┘
States: loading (role=status), empty (emptyKey), error (role=alert, reference code, Retry).
```

The dialog: title, the projected form, an error alert with the problem+json field errors, Cancel and Confirm. It
traps focus, restores focus to the opener on close, and closes on Escape through `cancel`.

## Build a screen on it

```ts
readonly columns: readonly ColumnDef<OrganizationRow>[] = [
  { key: 'name', headerKey: 'admin.organizations.columns.name', sortable: true },
  { key: 'code', headerKey: 'admin.organizations.columns.code' },
  { key: 'isActive', headerKey: 'admin.organizations.columns.status' },   // cell from <ng-template listCell="isActive">
];
readonly rowId = (row: OrganizationRow) => row.id;                       // the PublicId, never a database id
```

```html
<app-admin-list-pattern
  titleKey="admin.organizations.title" emptyKey="admin.organizations.empty"
  [columns]="columns" [rows]="store.items()" [total]="store.total()" [rowId]="rowId"
  [loading]="store.loading()" [error]="store.error()"
  [(page)]="store.page" [(search)]="store.search" [(sort)]="store.sort" (retry)="store.reload()">
  <dga-button listToolbar size="md" *hasPermission="PermissionKeys.OrganizationsManage" (click)="store.openCreate()">
    {{ 'admin.organizations.actions.create' | t }}
  </dga-button>
  <ng-template listCell="isActive" let-row>
    <dga-status-tag [tone]="row.isActive ? 'success' : 'neutral'">
      {{ (row.isActive ? 'admin.common.active' : 'admin.common.inactive') | t }}
    </dga-status-tag>
  </ng-template>
  <ng-template listRowActions let-row>
    <dga-button variant="subtle" size="sm" *hasPermission="PermissionKeys.OrganizationsManage" (click)="store.openEdit(row)">
      {{ 'admin.organizations.actions.edit' | t }}
    </dga-button>
  </ng-template>
</app-admin-list-pattern>

<app-editor-dialog-pattern
  [titleKey]="store.editing()?.id ? 'admin.organizations.edit-title' : 'admin.organizations.create-title'"
  [open]="store.editing() !== null" [busy]="store.saving()" [error]="store.saveError()"
  (confirm)="store.save()" (cancel)="store.closeEditor()">
  <dga-text-input [label]="'admin.organizations.fields.name' | t" required
    [(value)]="store.draftName" [error]="store.saveError() | fieldError: 'name'" />
</app-editor-dialog-pattern>
```

The screen's store reloads in an `effect()` (or a resource) that reads `page()`, `search()` and `sort()`. A new
search or sort resets the page to 1; `search` arrives trimmed after 300 ms (`SEARCH_DEBOUNCE_MS`) or at once on
Enter.

## Contract

| List input / model | Meaning |
| --- | --- |
| `titleKey` (required), `emptyKey`, `searchLabelKey` | Translation keys of the screen |
| `columns` (required) | `ColumnDef<T>[]`: `key`, `headerKey`, optional `cell`, `value(row)`, `sortable` |
| `rows`, `total`, `rowId` (required), `pageSize` | One server page, the total count and the row's PublicId |
| `loading`, `error`, `translatedCodes` | State; `error` is the `ApiError` (its reference code is shown) |
| `page`, `search`, `sort` (models) | 1-based page, trimmed search text, `SortState` or `null` (server default) |
| `searchable` | `false` hides the search box |
| `retry` (output) | The user pressed Retry in the error state |

Slots: `[listToolbar]`, `[listFilters]`, `<ng-template listCell="<key>">`, `<ng-template listRowActions>`.

| Dialog input | Meaning |
| --- | --- |
| `open` | Controlled: the dialog never closes itself |
| `titleKey` (required), `confirmKey`, `cancelKey` | Translation keys; defaults Save / Cancel |
| `busy`, `confirmDisabled`, `destructive` | Confirm state; `destructive` uses the error tone |
| `error`, `fieldErrors`, `translatedCodes` | The save's `ApiError` (field errors feed the alert and the `fieldError` pipe), or a client-side `FieldErrorMap` |
| `confirm`, `cancel` (outputs) | Save; Cancel button, close icon or Escape |

## Rules

- The screen gates the toolbar and every row action with `*hasPermission`; the route with `permissionGuard()`.
- Rows are identified by PublicId. Never show or route by a database id.
- Search, sort and paging happen on the server; the list never sorts or filters `rows` itself.
- A successful `confirm` and every `cancel` must set `open` back to `false`, then reload the list.
- Field errors come from the API's problem+json (`fieldErrorsFrom`); client-side checks pass `[fieldErrors]`.
- Filters in `[listFilters]` use ng-dga form controls (`dga-select` once available) and reset the page to 1.

## Accessibility

Search as a `role="search"` form, a table with a (visually hidden) caption from the title, `aria-sort` on sortable
headers, `aria-current="page"` in the pager and a polite page summary, loading as `role="status"`, errors as
`role="alert"`. The dialog is a native
modal `dialog` with a focus trap, focus restore and Escape; its error summary (with the field errors) is a
`role="alert"`, and each field shows its own message through the `fieldError` pipe.
