import { Directive, TemplateRef, inject, input } from '@angular/core';
import type { ListCellContext } from './list-columns';

/** A column's cell template: `<ng-template opsListCell="status" let-row>...</ng-template>`. */
@Directive({ selector: 'ng-template[opsListCell]' })
export class OpsListCellDirective<T> {
  readonly opsListCell = input.required<string>();
  readonly template = inject<TemplateRef<ListCellContext<T>>>(TemplateRef);
}
