import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, booleanAttribute, computed, contentChildren, input, output, type TemplateRef } from '@angular/core';
import { DgaButtonComponent, DgaIconComponent } from '@codelab/ng-dga';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { cellText, cellValue, type ColumnDef, type ListCellContext } from './list-columns';
import { OpsListCellDirective } from './ops-list-cell.directive';

/**
 * The console's read-only list: the admin-list page pattern's table half (captioned native table, column defs,
 * projected cell templates, loading, empty and error states with the failure's reference code and a retry button)
 * without search, sort and paging. It renders state and forwards intent; the screen's store owns the data.
 *
 * Inputs: `captionKey`, `columns`, `rows`, `rowId`, `loading`, `error`, `emptyKey`. Output: `retry`.
 */
@Component({
  selector: 'app-ops-list',
  imports: [DgaButtonComponent, DgaIconComponent, NgTemplateOutlet, TranslatePipe],
  templateUrl: './ops-list.component.html',
  styleUrl: './ops-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpsListComponent<T> {
  /** Translation key of the table caption (visually hidden; the page heading is visible). */
  readonly captionKey = input.required<string>();
  readonly columns = input.required<readonly ColumnDef<T>[]>();
  readonly rows = input<readonly T[]>([]);
  readonly rowId = input.required<(row: T) => string | number>();
  readonly loading = input(false, { transform: booleanAttribute });
  readonly error = input<ApiError | null>(null);
  readonly emptyKey = input<string>(CommonKeys.List.Empty);
  readonly retry = output<void>();

  private readonly cells = contentChildren(OpsListCellDirective);

  protected readonly keys = CommonKeys;
  readonly cellTemplates = computed(
    () => new Map(this.cells().map((cell) => [cell.opsListCell(), cell.template as TemplateRef<ListCellContext<T>>])),
  );
  readonly errorMessageKey = computed(() => {
    const error = this.error();
    return error ? apiErrorMessageKey(error) : null;
  });
  readonly showEmpty = computed(() => !this.loading() && !this.error() && this.rows().length === 0);
  readonly showLoadingRow = computed(() => this.loading() && this.rows().length === 0);

  templateFor(column: ColumnDef<T>): TemplateRef<ListCellContext<T>> | null {
    return this.cellTemplates().get(column.key) ?? null;
  }

  cellContext(column: ColumnDef<T>, row: T): ListCellContext<T> {
    return { $implicit: row, value: cellValue(column, row) };
  }

  cellTextOf(column: ColumnDef<T>, row: T): string {
    return cellText(cellValue(column, row));
  }
}
