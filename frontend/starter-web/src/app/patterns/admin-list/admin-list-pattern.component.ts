import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  booleanAttribute,
  computed,
  contentChild,
  contentChildren,
  inject,
  input,
  linkedSignal,
  model,
  output,
  type TemplateRef,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { DgaButtonComponent, DgaIconComponent, DgaTextInputComponent } from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { PatternTranslationKeys } from '../pattern-translation-keys';
import { ListCellDirective, ListRowActionsDirective } from './admin-list-templates.directive';
import { ListPagerComponent } from './list-pager.component';
import {
  SortDirection,
  ariaSort,
  cellText,
  cellValue,
  nextSort,
  type ColumnDef,
  type ListCellContext,
  type SortState,
} from './list-columns';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from './paging';

/** How long the search box waits after the last keystroke before it updates `search` (milliseconds). */
export const SEARCH_DEBOUNCE_MS = 300;

/**
 * The admin-list page pattern (P4 pattern "admin list + dialog editor", list half): a titled, searchable, filterable,
 * sortable, paged table with per-row actions and loading, empty and error states. It renders state and forwards user
 * intent; the screen owns the data and calls the API when `page`, `search` or `sort` change.
 *
 * Inputs: `titleKey`, `columns`, `rows`, `rowId`, `total`, `pageSize`, `loading`, `error`, `searchable`,
 * `searchLabelKey`, `emptyKey`. Models (two-way): `page` (1-based), `search` (debounced {@link SEARCH_DEBOUNCE_MS};
 * Enter applies it at once), `sort`. A new search or sort resets `page` to 1. Output: `retry` (the error state's button).
 *
 * Slots: `[listToolbar]` (actions beside the title, such as "New user"), `[listFilters]` (filter controls beside the
 * search box), `<ng-template listCell="key" let-row>` (a column's cell) and `<ng-template listRowActions let-row>`
 * (the actions column).
 */
@Component({
  selector: 'app-admin-list-pattern',
  imports: [DgaButtonComponent, DgaIconComponent, DgaTextInputComponent, ListPagerComponent, NgTemplateOutlet, TranslatePipe],
  templateUrl: './admin-list-pattern.component.html',
  styleUrl: './admin-list-pattern.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminListPatternComponent<T> {
  /** Translation key of the page title (also the table caption). */
  readonly titleKey = input.required<string>();
  readonly columns = input.required<readonly ColumnDef<T>[]>();
  readonly rows = input<readonly T[]>([]);
  /** A stable id per row (its PublicId), used to track rows across reloads. */
  readonly rowId = input.required<(row: T) => string | number>();
  /** All rows on the server for the current search and filters. */
  readonly total = input(0);
  readonly page = model(FIRST_PAGE);
  readonly pageSize = input(DEFAULT_PAGE_SIZE);
  readonly loading = input(false, { transform: booleanAttribute });
  /** The failure of the last load; shows the error state with its reference code. */
  readonly error = input<ApiError | null>(null);
  /**
   * The codes this screen has `errors.<CODE>` text for; any other code shows the generic message. Left unset, the
   * platform codes of ng-security are used. A module screen binds its own set (platform codes plus module codes).
   */
  readonly translatedCodes = input<ReadonlySet<string> | undefined>(undefined);
  readonly searchable = input(true, { transform: booleanAttribute });
  readonly search = model('');
  readonly searchLabelKey = input<string>(PatternTranslationKeys.List.Search);
  readonly sort = model<SortState | null>(null);
  readonly emptyKey = input<string>(PatternTranslationKeys.List.Empty);
  readonly retry = output<void>();

  private readonly cells = contentChildren(ListCellDirective);
  private readonly rowActions = contentChild(ListRowActionsDirective);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly keys = PatternTranslationKeys;
  protected readonly ascending = SortDirection.Ascending;
  protected readonly titleId = `app-admin-list-title-${nextListId()}`;
  /** What the search box shows; follows `search` when the screen changes it. */
  readonly searchText = linkedSignal(() => this.search());
  readonly cellTemplates = computed(() => new Map(this.cells().map((cell) => [cell.listCell(), cell.template])));
  readonly actionsTemplate = computed(() => this.rowActions()?.template ?? null);
  readonly columnCount = computed(() => this.columns().length + (this.actionsTemplate() ? 1 : 0));
  readonly errorMessageKey = computed(() => {
    const error = this.error();
    return error ? apiErrorMessageKey(error, this.translatedCodes()) : null;
  });
  readonly showEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);
  readonly showLoadingRow = computed(() => this.loading() && this.rows().length === 0);

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.searchTimer));
  }

  /** The template for a column's cells, or null for plain text. */
  templateFor(column: ColumnDef<T>): TemplateRef<ListCellContext<T>> | null {
    return column.cell ?? (this.cellTemplates().get(column.key) as TemplateRef<ListCellContext<T>> | undefined) ?? null;
  }

  cellContext(column: ColumnDef<T>, row: T): ListCellContext<T> {
    return { $implicit: row, value: cellValue(column, row) };
  }

  cellTextOf(column: ColumnDef<T>, row: T): string {
    return cellText(cellValue(column, row));
  }

  ariaSortOf(column: ColumnDef<T>): string | null {
    return ariaSort(this.sort(), column);
  }

  onSearchInput(text: string): void {
    this.searchText.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.applySearch(), SEARCH_DEBOUNCE_MS);
  }

  /** Enter in the search box: apply now, without waiting for the debounce. */
  applySearch(event?: Event): void {
    event?.preventDefault();
    clearTimeout(this.searchTimer);
    const text = this.searchText().trim();
    if (text !== this.search()) {
      this.search.set(text);
      this.page.set(FIRST_PAGE);
    }
  }

  toggleSort(column: ColumnDef<T>): void {
    if (!column.sortable) {
      return;
    }
    this.sort.set(nextSort(this.sort(), column.key));
    this.page.set(FIRST_PAGE);
  }
}

let listCount = 0;

function nextListId(): number {
  listCount += 1;
  return listCount;
}
