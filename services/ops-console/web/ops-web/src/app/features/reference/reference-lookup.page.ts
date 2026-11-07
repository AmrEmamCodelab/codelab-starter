import { ChangeDetectionStrategy, Component, inject, linkedSignal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  DgaAlertComponent,
  DgaButtonComponent,
  DgaIconComponent,
  DgaInfoFieldComponent,
  DgaTagComponent,
  DgaTextInputComponent,
} from '@codelab/ng-dga';
import type { ReferenceEvent } from '../../core/api/ops-api.models';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { DateTimePipe } from '../../core/i18n/date-time';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { QueryParams } from '../../core/routing/route-paths';
import type { ColumnDef } from '../../patterns/ops-list/list-columns';
import { OpsListCellDirective } from '../../patterns/ops-list/ops-list-cell.directive';
import { OpsListComponent } from '../../patterns/ops-list/ops-list.component';
import { REFERENCE_INPUT_MAX_LENGTH, ReferenceKeys, levelTone } from './reference-code';
import { CopyOutcome, ReferenceLookupStore } from './reference-lookup.store';

/**
 * Reference lookup: an operator types the reference code a user read from an error dialog and sees the matching
 * request, its log events and a link to the trace in Seq (open or copy). The code lives in the URL (`?code=`), so a
 * lookup is a link a colleague can open.
 */
@Component({
  selector: 'app-reference-lookup-page',
  imports: [
    DateTimePipe,
    DgaAlertComponent,
    DgaButtonComponent,
    DgaIconComponent,
    DgaInfoFieldComponent,
    DgaTagComponent,
    DgaTextInputComponent,
    OpsListCellDirective,
    OpsListComponent,
    TranslatePipe,
  ],
  providers: [ReferenceLookupStore],
  templateUrl: './reference-lookup.page.html',
  styleUrl: './reference-lookup.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReferenceLookupPage {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly store = inject(ReferenceLookupStore);
  protected readonly keys = ReferenceKeys;
  protected readonly common = CommonKeys;
  protected readonly copied = CopyOutcome.Copied;
  protected readonly copyFailed = CopyOutcome.Failed;
  protected readonly maxLength = REFERENCE_INPUT_MAX_LENGTH;
  protected readonly levelTone = levelTone;
  /** What the box shows; follows the looked-up code when the URL changes. */
  protected readonly query = linkedSignal(() => this.store.code() ?? '');
  protected readonly columns: readonly ColumnDef<ReferenceEvent>[] = [
    { key: 'timestamp', headerKey: ReferenceKeys.Columns.Timestamp },
    { key: 'level', headerKey: ReferenceKeys.Columns.Level },
    { key: 'message', headerKey: ReferenceKeys.Columns.Message, ltr: true },
  ];
  protected readonly rowId = (event: ReferenceEvent): string => `${event.timestamp}|${event.message}`;

  constructor() {
    this.route.queryParamMap
      .pipe(takeUntilDestroyed())
      .subscribe((params) => this.store.lookup(params.get(QueryParams.Code)));
  }

  onSubmit(event: Event): void {
    event.preventDefault();
    const code = this.store.submit(this.query());
    if (code === null) {
      return;
    }
    this.store.lookup(code);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { [QueryParams.Code]: code } });
  }
}
