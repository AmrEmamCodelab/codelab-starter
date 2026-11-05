import { Directive, TemplateRef, inject, input } from '@angular/core';
import type { ListCellContext, ListRowContext } from './list-columns';

/**
 * A cell template for one column, projected into `<app-admin-list-pattern>`; `listCell` is the column `key`.
 * Use it when the template lives in the screen's own HTML (a {@link ColumnDef.cell} template ref wins over it).
 * `let-row` is untyped (`any`) unless `[listCellOf]="rows"` names the rows, which types it as the row type.
 *
 * @example
 * <ng-template listCell="isActive" let-row>
 *   <dga-status-tag [status]="row.isActive ? 'success' : 'neutral'">{{ ... | t }}</dga-status-tag>
 * </ng-template>
 */
@Directive({ selector: 'ng-template[listCell]' })
export class ListCellDirective<T = any> {
  readonly listCell = input.required<string>();
  /** Optional, for template type checking only: the rows the list shows, so `let-row` is typed. */
  readonly listCellOf = input<readonly T[] | null>(null);
  readonly template = inject<TemplateRef<ListCellContext<T>>>(TemplateRef);

  static ngTemplateContextGuard<T>(_directive: ListCellDirective<T>, context: unknown): context is ListCellContext<T> {
    return true;
  }
}

/**
 * The per-row actions (edit, deactivate...), projected into `<app-admin-list-pattern>`; it renders in the last column.
 * Gate every action with `*hasPermission`. `[listRowActionsOf]="rows"` types `let-row` (otherwise `any`).
 *
 * @example
 * <ng-template listRowActions let-row>
 *   <dga-button *hasPermission="PermissionKeys.UsersManage" variant="subtle" size="sm" (click)="edit(row)">...</dga-button>
 * </ng-template>
 */
@Directive({ selector: 'ng-template[listRowActions]' })
export class ListRowActionsDirective<T = any> {
  /** Optional, for template type checking only: the rows the list shows, so `let-row` is typed. */
  readonly listRowActionsOf = input<readonly T[] | null>(null);
  readonly template = inject<TemplateRef<ListRowContext<T>>>(TemplateRef);

  static ngTemplateContextGuard<T>(_directive: ListRowActionsDirective<T>, context: unknown): context is ListRowContext<T> {
    return true;
  }
}
