import type { TemplateRef } from '@angular/core';

/** Sort direction of a list column. Integers, like every enum the starter owns. */
export enum SortDirection {
  Ascending = 1,
  Descending = 2,
}

/** The list's current sort: which column and which way. `null` means the server's default order. */
export interface SortState {
  readonly key: string;
  readonly direction: SortDirection;
}

/** What a cell template receives: `<ng-template let-row let-value="value">`. */
export interface ListCellContext<T> {
  readonly $implicit: T;
  readonly value: unknown;
}

/** What the row-actions template receives: `<ng-template listRowActions let-row>`. */
export interface ListRowContext<T> {
  readonly $implicit: T;
}

/**
 * One column of the admin list.
 *
 * - `key` identifies the column (sort key, cell-template match, default value lookup `row[key]`).
 * - `headerKey` is a translation key (the pattern translates it; never raw text).
 * - The cell shows, in order of precedence: the `cell` template, a projected `<ng-template listCell="key">`, the
 *   `value(row)` accessor, or `row[key]` as text.
 * - `sortable` makes the header a sort button; the pattern emits the new {@link SortState} and the screen reloads.
 */
export interface ColumnDef<T> {
  readonly key: string;
  readonly headerKey: string;
  readonly cell?: TemplateRef<ListCellContext<T>>;
  readonly value?: (row: T) => unknown;
  readonly sortable?: boolean;
}

/** The value a column shows for a row: its accessor, else the row's property of the same name. */
export function cellValue<T>(column: ColumnDef<T>, row: T): unknown {
  if (column.value) {
    return column.value(row);
  }
  if (row !== null && typeof row === 'object') {
    return (row as Record<string, unknown>)[column.key];
  }
  return undefined;
}

/** A cell value as display text: null and undefined become an empty string. */
export function cellText(value: unknown): string {
  return value === null || value === undefined ? '' : String(value);
}

/**
 * The sort after a click on `key`'s header: a new column starts ascending, the same column flips, and a third
 * click is not a state (it flips back to ascending), so the user always sees an explicit order once they chose one.
 */
export function nextSort(current: SortState | null, key: string): SortState {
  if (current?.key !== key) {
    return { key, direction: SortDirection.Ascending };
  }
  const direction = current.direction === SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
  return { key, direction };
}

/** The `aria-sort` value for a header (`none` on a sortable column that is not the sorted one). */
export function ariaSort<T>(current: SortState | null, column: ColumnDef<T>): 'ascending' | 'descending' | 'none' | null {
  if (!column.sortable) {
    return null;
  }
  if (current?.key !== column.key) {
    return 'none';
  }
  return current.direction === SortDirection.Ascending ? 'ascending' : 'descending';
}
