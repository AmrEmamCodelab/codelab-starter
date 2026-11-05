import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  contentChild,
  contentChildren,
  inject,
  input,
  model,
  output,
  viewChild,
  type TemplateRef,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { DgaButtonComponent, DgaCheckboxComponent, DgaIconComponent, DgaStatusTagComponent, DgaTabsComponent, type DgaTabValue } from '@codelab/ng-dga';
import type { ApiError } from '@codelab/ng-security';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK } from '../../core/session/clock';
import { AdminListPatternComponent } from '../admin-list/admin-list-pattern.component';
import { ListCellDirective, ListRowActionsDirective } from '../admin-list/admin-list-templates.directive';
import type { ColumnDef, ListCellContext, SortState } from '../admin-list/list-columns';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../admin-list/paging';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { TreeCheckState } from '../tree-editor/key-tree';
import {
  DUE_STATE_DISPLAY,
  NO_SELECTION,
  dueState,
  formatDueAt,
  pageSelectionState,
  queueTabsFor,
  setPageSelection,
  toggleSelection,
  type DueColumnDef,
  type QueueRowId,
  type QueueTab,
} from './queue-state';
import { WorkQueueTranslationKeys } from './work-queue-translation-keys';

/** The column key of the bulk-select column the pattern adds in front of the screen's columns. */
export const QUEUE_SELECT_COLUMN_KEY = 'queue-select';

/**
 * The work-queue page pattern: a staff inbox built on {@link AdminListPatternComponent} (search, sort, paging, loading,
 * empty and error states) plus queue tabs, a status-filter slot, an SLA/due column and optional bulk selection. It
 * renders state and forwards intent; the screen owns the tab list, loads the rows whenever `tab`, `page`, `search` or
 * `sort` change, and gates every action.
 *
 * Inputs: everything the admin list takes (`titleKey`, `columns`, `rows`, `rowId`, `total`, `pageSize`, `loading`,
 * `error`, `translatedCodes`, `searchable`, `searchLabelKey`, `emptyKey`), plus `tabs` (screen-provided
 * {@link QueueTab}s), `dueColumn` ({@link DueColumnDef}), `selectable` and `rowLabel` (the accessible name of a row's
 * checkbox). Models: `tab`, `page`, `search`, `sort`, `selected`. A tab change resets `page` to 1 and clears the
 * selection. Output: `retry`.
 *
 * Slots: `[queueToolbar]` (beside the title), `[queueFilters]` (the status filter and other filters beside the search
 * box), `[queueBulkActions]` (shown while rows are selected), `<ng-template listCell="key" let-row>` and
 * `<ng-template listRowActions let-row>` exactly as on the admin list.
 */
@Component({
  selector: 'app-work-queue-pattern',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaCheckboxComponent,
    DgaIconComponent,
    DgaStatusTagComponent,
    DgaTabsComponent,
    ListRowActionsDirective,
    NgTemplateOutlet,
    TranslatePipe,
  ],
  templateUrl: './work-queue-pattern.component.html',
  styleUrl: './work-queue-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkQueuePatternComponent<T> {
  readonly titleKey = input.required<string>();
  readonly columns = input.required<readonly ColumnDef<T>[]>();
  readonly rows = input<readonly T[]>([]);
  readonly rowId = input.required<(row: T) => QueueRowId>();
  readonly total = input(0);
  readonly pageSize = input(DEFAULT_PAGE_SIZE);
  readonly loading = input(false, { transform: booleanAttribute });
  readonly error = input<ApiError | null>(null);
  readonly translatedCodes = input<ReadonlySet<string> | undefined>(undefined);
  readonly searchable = input(true, { transform: booleanAttribute });
  readonly searchLabelKey = input<string>(PatternTranslationKeys.List.Search);
  readonly emptyKey = input<string>(PatternTranslationKeys.List.Empty);
  /** The queue views the screen offers; none hides the tab strip. */
  readonly tabs = input<readonly QueueTab[]>([]);
  readonly dueColumn = input<DueColumnDef<T> | null>(null);
  readonly selectable = input(false, { transform: booleanAttribute });
  /** The text that names a row for its checkbox ("Select REQ-0042"); defaults to the row id. */
  readonly rowLabel = input<((row: T) => string) | null>(null);
  readonly tab = model('');
  readonly page = model(FIRST_PAGE);
  readonly search = model('');
  readonly sort = model<SortState | null>(null);
  readonly selected = model<ReadonlySet<QueueRowId>>(NO_SELECTION);
  readonly retry = output<void>();

  private readonly translations = inject(TranslationService);
  private readonly clock = inject(APP_CLOCK);
  private readonly cells = contentChildren(ListCellDirective);
  private readonly rowActions = contentChild(ListRowActionsDirective);
  private readonly selectCell = viewChild<TemplateRef<ListCellContext<T>>>('selectCell');
  private readonly dueCell = viewChild<TemplateRef<ListCellContext<T>>>('dueCell');

  protected readonly keys = WorkQueueTranslationKeys;
  protected readonly checked = TreeCheckState.Checked;
  protected readonly mixed = TreeCheckState.Mixed;
  readonly actionsTemplate = computed(() => this.rowActions()?.template ?? null);
  readonly dgaTabs = computed(() => queueTabsFor(this.tabs(), (key, params) => this.translations.translate(key, params)));
  /** The clock at the last reload: due states refresh whenever the rows change. */
  readonly now = computed(() => {
    this.rows();
    return this.clock();
  });
  readonly pageIds = computed(() => this.rows().map((row) => this.rowId()(row)));
  readonly pageState = computed(() => pageSelectionState(this.selected(), this.pageIds()));
  readonly selectedCount = computed(() => this.selected().size);
  readonly showSelectPage = computed(() => this.selectable() && this.rows().length > 0);
  readonly listColumns = computed<readonly ColumnDef<T>[]>(() => {
    const select = this.selectable() ? [this.selectColumn()] : [];
    const due = this.dueColumn();
    return [...select, ...this.screenColumns(), ...(due ? [this.dueListColumn(due)] : [])];
  });

  onTabChange(value: DgaTabValue): void {
    const next = String(value);
    if (next === this.tab()) {
      return;
    }
    this.tab.set(next);
    this.page.set(FIRST_PAGE);
    this.selected.set(NO_SELECTION);
  }

  isSelected(row: T): boolean {
    return this.selected().has(this.rowId()(row));
  }

  toggleRow(row: T): void {
    this.selected.set(toggleSelection(this.selected(), this.rowId()(row)));
  }

  onSelectPage(selected: boolean): void {
    this.selected.set(setPageSelection(this.selected(), this.pageIds(), selected));
  }

  clearSelection(): void {
    this.selected.set(NO_SELECTION);
  }

  rowLabelOf(row: T): string {
    const label = this.rowLabel();
    return label ? label(row) : String(this.rowId()(row));
  }

  dueStateOf(value: unknown) {
    const state = dueState(asDueText(value), this.now(), this.dueColumn()?.soonWindowMs);
    return state === null ? null : DUE_STATE_DISPLAY[state];
  }

  dueTextOf(value: unknown): string {
    return formatDueAt(asDueText(value), this.translations.language());
  }

  private screenColumns(): readonly ColumnDef<T>[] {
    const projected = new Map(this.cells().map((cell) => [cell.listCell(), cell.template as TemplateRef<ListCellContext<T>>]));
    return this.columns().map((column) => {
      const template = projected.get(column.key);
      return column.cell || !template ? column : { ...column, cell: template };
    });
  }

  private selectColumn(): ColumnDef<T> {
    return { key: QUEUE_SELECT_COLUMN_KEY, headerKey: WorkQueueTranslationKeys.SelectColumn, cell: this.selectCell() };
  }

  private dueListColumn(due: DueColumnDef<T>): ColumnDef<T> {
    return { key: due.key, headerKey: due.headerKey, sortable: due.sortable, value: due.dueAt, cell: this.dueCell() };
  }
}

function asDueText(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}
