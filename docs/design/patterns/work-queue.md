# Pattern: work queue

**Use it for** a staff inbox: work items waiting for someone to act, split into views such as "assigned to me", "my
team" and "all", filtered by status, with a due (SLA) column and optional bulk actions (assign, release). A
requester's own list of requests is the same pattern without assignment tabs and without bulk selection.

**Do not use it for** administration of reference records (use the [admin list](admin-list.md)) or one record's
detail (use the [staff detail](staff-detail.md)).

| Part | Selector | Code |
| --- | --- | --- |
| Queue | `app-work-queue-pattern` | `src/app/patterns/work-queue/work-queue-pattern.component.*` |
| Tabs, due state, selection helpers | `QueueTab`, `DueColumnDef`, `DueState`, `dueState`, `toggleSelection`... | `src/app/patterns/work-queue/queue-state.ts` |
| Keys | `WorkQueueTranslationKeys` (`patterns.work-queue.*`) | `src/app/patterns/work-queue/work-queue-translation-keys.ts` |

The queue is built on the admin list: search, sortable columns, paging, cell and row-action templates, and the
loading, empty and error states behave exactly as in [admin-list.md](admin-list.md).

## Anatomy

```
┌───────────────────────────────────────────────────────────────────┐
│ Title                                        [toolbar slot]      │  titleKey + [queueToolbar]
│ [search ......] [status filter slot]                             │  debounced search + [queueFilters]
│ Assigned to me (4) │ My team │ All                               │  tabs: screen's QueueTab[] (dga-tabs)
│ [x] Select all on this page   3 selected  [bulk slot]  Clear     │  only when selectable; [queueBulkActions]
├───────────────────────────────────────────────────────────────────┤
│ ☐ │ Reference ▲ │ Status          │ Due                 │ Actions │  select column, ColumnDef[], due column
│ ☐ │ R-0042      │ <dga-status-tag>│ 10 Mar 14:00 Overdue│ [Open]  │
├───────────────────────────────────────────────────────────────────┤
│ 21–40 of 95          ‹ 1 … 2 [3] 4 … 5 ›                          │  pager
└───────────────────────────────────────────────────────────────────┘
```

## Build a screen on it

```ts
readonly tabs = computed<readonly QueueTab[]>(() => [
  { value: QueueViews.Mine, labelKey: 'tasks.tabs.mine', count: this.store.counts()?.mine },
  ...(this.permissions.has(PermissionKeys.TasksViewTeam) ? [{ value: QueueViews.Team, labelKey: 'tasks.tabs.team' }] : []),
]);
readonly columns: readonly ColumnDef<TaskRow>[] = [
  { key: 'reference', headerKey: 'tasks.columns.reference', sortable: true },
  { key: 'status', headerKey: 'tasks.columns.status' },                  // cell from <ng-template listCell="status">
];
readonly dueColumn: DueColumnDef<TaskRow> = { key: 'dueAt', headerKey: 'tasks.columns.due', dueAt: (row) => row.dueAt, sortable: true };
readonly rowId = (row: TaskRow) => row.id;                               // the PublicId
readonly rowLabel = (row: TaskRow) => row.reference;                     // names the row's checkbox
```

```html
<app-work-queue-pattern
  titleKey="tasks.title" emptyKey="tasks.empty" selectable
  [tabs]="tabs()" [columns]="columns" [dueColumn]="dueColumn" [rowId]="rowId" [rowLabel]="rowLabel"
  [rows]="store.items()" [total]="store.total()" [loading]="store.loading()" [error]="store.error()"
  [(tab)]="store.view" [(page)]="store.page" [(search)]="store.search" [(sort)]="store.sort" [(selected)]="store.selected"
  (retry)="store.reload()">
  <dga-select queueFilters [label]="'tasks.filters.status' | t" [options]="statusOptions()" [(value)]="store.status" />
  <dga-button queueBulkActions size="sm" *hasPermission="PermissionKeys.TasksAssign" (click)="store.assignSelected()">
    {{ 'tasks.actions.assign' | t }}
  </dga-button>
  <ng-template listCell="status" let-row>
    <dga-status-tag [tone]="statusTone(row.statusId)">{{ statusLabelKey(row.statusId) | t }}</dga-status-tag>
  </ng-template>
  <ng-template listRowActions let-row>
    <dga-button variant="subtle" size="sm" *hasPermission="PermissionKeys.TasksView" (click)="open(row)">
      {{ 'tasks.actions.open' | t }}
    </dga-button>
  </ng-template>
</app-work-queue-pattern>
```

The store reloads in an `effect()` (or a resource) that reads `view()`, `status()`, `page()`, `search()` and
`sort()`. A tab change resets the page to 1 and clears the selection; a status filter change should reset the page
too (the screen owns that filter).

## Contract

Everything in the admin-list contract (inputs, `page` / `search` / `sort` models, `retry`, `listCell`,
`listRowActions`), plus:

| Input / model | Meaning |
| --- | --- |
| `tabs` | `QueueTab[]`: `value` (the screen's view constant), `labelKey`, optional `count`, `disabled`. Empty hides the strip |
| `tab` (model) | The selected view's `value`; a change resets `page` and `selected` |
| `dueColumn` | `DueColumnDef<T>`: `key`, `headerKey`, `dueAt(row)` (ISO 8601 or null), `soonWindowMs` (default 1 day), `sortable` |
| `selectable`, `rowLabel` | Adds the select column and the page checkbox; `rowLabel(row)` names each row checkbox |
| `selected` (model) | `ReadonlySet` of row ids (PublicIds); kept across pages, cleared on a tab change |

Slots: `[queueToolbar]`, `[queueFilters]`, `[queueBulkActions]` (shown while rows are selected).

Due states (`DueState`, integers): `OnTrack = 1` (neutral tag), `DueSoon = 2` (warning, within `soonWindowMs`),
`Overdue = 3` (error, at or after the due time). A row without a due time shows "No due date". The clock is
`APP_CLOCK`, read when the rows change, so the states refresh on every reload.

## Rules

- The server computes the due time from the deadline rules and filters by tab and status; the queue never filters
  or sorts `rows` itself.
- Tabs are views the user is allowed to see: build the list from the permission store.
- Bulk actions are gated with `*hasPermission`; the server re-checks every selected id.
- Status labels and tones come from the screen's lookup constants and i18n, never from translated text.

## Accessibility

Everything in the admin list (search landmark, caption, `aria-sort`, pager, states), plus: the WAI-ARIA tabs pattern
from `dga-tabs` (arrow keys follow the visual order and mirror in RTL, Home/End, roving tab index), a named checkbox
per row ("Select R-0042"), a page checkbox with a mixed state, the selected count as `role="status"`, and the due
time as a `time` element with its machine-readable `datetime`.
