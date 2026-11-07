import type { TemplateRef } from '@angular/core';

/** What a cell template receives: `<ng-template opsListCell="key" let-row let-value="value">`. */
export interface ListCellContext<T> {
  readonly $implicit: T;
  readonly value: unknown;
}

/**
 * One column of the read-only ops list (the admin-list pattern's column shape, without sorting: the console's lists are
 * short and the server orders them).
 *
 * - `key` identifies the column (cell-template match, default lookup `row[key]`).
 * - `headerKey` is a translation key; the pattern translates it.
 * - The cell shows a projected `<ng-template opsListCell="key">`, else `value(row)`, else `row[key]`, as text.
 * - `ltr` keeps codes, paths and names left-to-right inside RTL text.
 */
export interface ColumnDef<T> {
  readonly key: string;
  readonly headerKey: string;
  readonly value?: (row: T) => unknown;
  readonly ltr?: boolean;
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

/** Template lookup by column key. */
export type CellTemplates<T> = ReadonlyMap<string, TemplateRef<ListCellContext<T>>>;
