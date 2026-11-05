# Web page patterns (`src/app/patterns`)

## Purpose

Reusable page bases that screens are built on, so every admin screen looks and behaves the same and a new screen is
mostly configuration. A pattern renders state and forwards user intent; the screen owns the data, the API calls
(through its feature client) and the permission gates. All eight page patterns live here (admin list + editor dialog,
tree editor, work queue, staff detail + decision dialog, multi-step wizard, requester detail + status tracker, portal
content, landing dashboard; see `docs/design/page-patterns.md`). Each has a design doc in
`docs/design/patterns/` and a live example on the `/showcase` pages (`config.json` `showcase: true`).

| Pattern / helper | What it gives a screen | Files |
| --- | --- | --- |
| `app-admin-list-pattern` | Title + toolbar slot, debounced search, filter slot, sortable table from typed `ColumnDef<T>`, cell and row-action templates, loading / empty / error (with reference code) states, pager | `admin-list/` |
| `app-list-pager` | "21–40 of 95", previous/next and numbered pages with gaps; 1-based `[(page)]` | `admin-list/list-pager.component.*` |
| Paging math | `pageCount`, `clampPage`, `pageRange`, `pagerItems`, `DEFAULT_PAGE_SIZE`, `PAGER_WINDOW` | `admin-list/paging.ts` |
| Columns | `ColumnDef<T>`, `SortState`, `SortDirection`, `cellValue`, `nextSort`, `ariaSort` | `admin-list/list-columns.ts` |
| `app-editor-dialog-pattern` | Controlled modal native `<dialog>` around a projected form: title, confirm/cancel, busy, error alert with reference code and field-error list, focus trap and restore, Escape | `editor-dialog/` |
| Field errors | `FieldErrorMap`, `fieldErrorsFrom(apiError)`, `firstFieldError`, the `fieldError` pipe | `editor-dialog/field-errors.ts`, `field-error.pipe.ts` |
| `app-tree-editor-pattern` | Checkbox tree from dotted keys or explicit nodes: tri-state parents, filter keeping ancestors, expand/collapse all, select/clear all, read-only, `[(value)]` set of keys, `selectionChange` with added/removed | `tree-editor/` |
| Tree helpers | `buildKeyTree(keys, labelFor)`, `checkState`, `toggleSubtree`, `filterTree`, `diffSelection`, `TreeCheckState` | `tree-editor/key-tree.ts` |
| Translation keys | `PatternTranslationKeys` (everything under `patterns.*`) | `pattern-translation-keys.ts` |
| `app-work-queue-pattern` | Staff inbox on the admin list: queue tabs (`QueueTab`, `[(tab)]`), `[queueFilters]` slot, SLA/due column (`DueColumnDef`, `DueState` against `APP_CLOCK`), optional bulk selection (`[(selected)]`, `[queueBulkActions]`); helpers in `queue-state.ts` | `work-queue/` |
| `app-staff-detail-pattern` | Record page for staff: back/status/header-action slots, detail cards, history slot, decision panel from server `AllowedAction[]` emitting `decide(action)`, loading/error | `staff-detail/` |
| `app-decision-dialog` | Reason dialog on the editor dialog: mandatory reason (`DECISION_REASON_MIN_LENGTH`/`MAX_LENGTH`), counter, confirm emits the trimmed reason, server field errors under the field | `decision-dialog/` |
| `app-portal-content-pattern` + `[portalSection]` | Public page frame: `[portalHeader]` (or `titleKey`), table of contents from `PortalSection[]` with in-page anchors and focus move, sections slot, `[portalLastUpdated]` | `portal-content/` |
| `app-landing-dashboard-pattern` | Greeting/`[landingHero]`, KPI tiles from `KpiTile[]`, quick actions from `QuickAction[]`, `[landingRecent]` | `landing-dashboard/` |
| `app-wizard-pattern` | Multi-step form base: navigable dga-stepper trail, one `<ng-template wizardStep="id">` per step, optional review step, Back / Next / Save draft / Submit / Cancel, guards on each step's `valid` signal, server field errors mapped to steps through `fieldSteps` | `wizard/` |
| `app-status-tracker` | Current-status card plus a read-only timeline of `StatusHistoryEntry`; `toneFor`, `order`, `current` | `status-tracker/` |
| `app-requester-detail-pattern` | Requester's record page: header, `[detailBanner]`, `[detailSummary]`, `[detailAttachments]`, status tracker and `[detailActions]`, loading and error | `requester-detail/` |

Import from the barrel: `import { AdminListPatternComponent, type ColumnDef } from '../../../patterns';`.

## Use

### Admin list

```ts
readonly columns: readonly ColumnDef<UserListItem>[] = [
  { key: 'userName', headerKey: 'admin.users.columns.user-name', sortable: true },
  { key: 'organizationName', headerKey: 'admin.users.columns.organization' },
  { key: 'isActive', headerKey: 'admin.users.columns.status' },        // cell from <ng-template listCell="isActive">
];
readonly rowId = (row: UserListItem) => row.id;                        // the PublicId
```

```html
<app-admin-list-pattern
  titleKey="admin.users.title"
  [columns]="columns" [rows]="page().items" [total]="page().total" [rowId]="rowId"
  [loading]="loading()" [error]="loadError()"
  [(page)]="pageNumber" [(search)]="search" [(sort)]="sort"
  (retry)="reload()">
  <dga-button listToolbar *hasPermission="PermissionKeys.UsersManage" (click)="create()">{{ 'admin.users.new' | t }}</dga-button>
  <dga-checkbox listFilters [label]="'admin.users.only-active' | t" [(checked)]="onlyActive" />
  <ng-template listCell="isActive" let-row>
    <dga-status-tag [status]="row.isActive ? 'success' : 'neutral'">{{ (row.isActive ? 'admin.users.active' : 'admin.users.inactive') | t }}</dga-status-tag>
  </ng-template>
  <ng-template listRowActions let-row>
    <dga-button *hasPermission="PermissionKeys.UsersManage" variant="subtle" size="sm" (click)="edit(row)">{{ 'admin.users.edit' | t }}</dga-button>
  </ng-template>
</app-admin-list-pattern>
```

The screen reloads in an `effect()` (or a resource) that reads `pageNumber()`, `search()` and `sort()`. A new search
or sort resets the page to 1. `search` arrives trimmed, after `SEARCH_DEBOUNCE_MS` (300 ms) or at once on Enter.

### Editor dialog

```html
<app-editor-dialog-pattern
  titleKey="admin.users.edit-title"
  [open]="editing() !== null" [busy]="saving()" [error]="saveError()"
  (confirm)="save()" (cancel)="editing.set(null)">
  <dga-text-input [label]="'admin.users.email' | t" [(value)]="email" [error]="saveError() | fieldError: 'email'" />
</app-editor-dialog-pattern>
```

The dialog is controlled: it never closes itself. `cancel` (button, close icon, Escape) and a successful `confirm`
must set `open` back to false. `error` is the `ApiError` the save failed with (`parseApiError(httpError)`, or the
error your client rethrew); its problem+json field errors are listed in the alert and feed the `fieldError` pipe.
Pass `[fieldErrors]` only to show a map of your own (client-side checks). Inputs: `open`, `titleKey`, `confirmKey`
(default "Save"), `cancelKey`, `busy`, `confirmDisabled`, `destructive`, `error`, `fieldErrors`. Outputs: `confirm`,
`cancel`.

### Tree editor

```html
<app-tree-editor-pattern
  labelKey="admin.access.roles.permissions"
  [keys]="catalogKeys()" [labelFor]="permissionLabel" [readOnly]="!canManage()"
  [(value)]="selectedKeys" (selectionChange)="dirty.set(true)" />
```

```ts
readonly permissionLabel: TreeLabelResolver = (key) => ({ textKey: `permissions.${key}` }); // or { text: description }
```

`value` holds selectable keys only (the given keys); group nodes (`access`, `access.roles`) show the state of the
keys below them. `nodes` (explicit `TreeNode[]`) wins over `keys`. Inputs: `labelKey`, `keys`, `nodes`, `labelFor`,
`readOnly`, `searchable`, `initiallyExpanded`. Model: `value`. Output: `selectionChange` (`added`, `removed`, `value`).

## Extend

- **A new pattern** gets its own folder (`patterns/<name>/`) with the component, its pure helpers in a separate
  `.ts` file, and a spec for each. The component spec renders it inside a host component that uses it the way a
  screen would. Add a live example to the `/showcase` patterns page and a design doc in `docs/design/patterns/`,
  export it from `index.ts` and add a row to the table above.
- **A new text** the pattern renders on its own goes in `PatternTranslationKeys` and in both `ar.json` and `en.json`
  under `patterns.*` (the spec checks both). Screen-specific text is an input (`titleKey`, `emptyKey`...), never a
  pattern key.
- **A new column feature** (alignment, width) is an optional `ColumnDef` member with a default, so existing screens
  keep compiling.

## Rules

- Patterns import only `core` and `@codelab/ng-dga` (ESLint boundaries also allow `shared`; keep patterns free of it
  so they stay generic). Never a feature or a layout.
- No API calls, no permission checks and no feature policy inside a pattern: the screen gates its toolbar and row
  actions with `*hasPermission` and loads the data.
- Standalone, OnPush, signal inputs/models/outputs. Native semantic elements (`table`, `dialog`, `fieldset`,
  `button`) styled with `--dga-*` tokens and logical CSS are allowed here; no third-party UI.
- Text colours on tinted surfaces (the current-status card, the active table-of-contents link) use
  `--dga-color-text-secondary` or `--dga-color-text-link`, never `-muted` or `-primary`: those fall below 4.5:1 there.
- Accessibility is part of the contract: captions and `aria-sort` on the table, `aria-current` in the pager,
  `role="alert"` for errors, `aria-expanded`/`aria-controls` on tree toggles, the native mixed state on tri-state
  boxes, and the dialog's focus trap and restore.
- Every pattern button carries `data-public-action`: the gate belongs to the screen that places the pattern.

## Gotchas

- Specs need the Angular JIT signal transform in `vitest.config.mts` (`starter-angular-signal-metadata`): without it
  JIT does not see `input()`, `model()`, `output()` or signal queries, and bindings are silently ignored.
- jsdom has no `HTMLDialogElement.showModal()`; the dialog falls back to the `open` attribute, so specs cover focus
  and Escape through the component's own handlers. In a browser, `showModal()` also makes the page behind inert.
- The dialog emits `cancel` once per Escape: the keydown handler and the native `cancel` event are de-duplicated.
- Generic `AdminListPatternComponent<T>` infers `T` from `[columns]`; type `columns` as `readonly ColumnDef<Row>[]`
  so `rowId` and accessors see the row type. A projected `<ng-template listCell>` / `listRowActions` cannot see `T`:
  its `let-row` is `any` unless you add `[listCellOf]="rows()"` / `[listRowActionsOf]="rows()"` (type checking
  only), which types it as the row and lets strict templates catch a misspelt member.
- While the tree filter is active every visible branch is expanded and expand/collapse all is disabled; clearing the
  filter restores the user's own expansion.
- Select all / clear all act on the VISIBLE keys, so with a filter they select only the matches.
- Each P4 pattern keeps its texts in `<pattern>-translation-keys.ts` under `patterns.<name>.*`, included in PATTERN_TRANSLATION_KEYS. Full examples: docs/design/patterns/. Only the wizard's current step template is rendered: keep entered values in the screen's form or store.
