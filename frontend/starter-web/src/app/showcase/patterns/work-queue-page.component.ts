import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DgaButtonComponent, DgaSelectComponent, DgaStatusTagComponent, type DgaSelectOption } from '@codelab/ng-dga';
import { Router } from '@angular/router';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { TranslationService } from '../../core/i18n/translation.service';
import { APP_CLOCK } from '../../core/session/clock';
import {
  ListCellDirective,
  ListRowActionsDirective,
  NO_SELECTION,
  WorkQueuePatternComponent,
  type ColumnDef,
  type DueColumnDef,
  type QueueRowId,
  type SortState,
} from '../../patterns';
import { ShowcaseFrameComponent } from '../frame/showcase-frame.component';
import { ShowcaseState, showcaseListView } from '../frame/showcase-state';
import { ShowcaseStateSwitchComponent } from '../frame/showcase-state-switch.component';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';
import {
  QUEUE_STATUS_DISPLAY,
  QueueStatus,
  QueueView,
  applyListQuery,
  queueRows,
  queueTabs,
  type QueueRow,
} from './pattern-fixtures';

const P = ShowcaseTranslationKeys.Patterns;

/** The work-queue column keys of the demo. */
export const QueueColumn = { Reference: 'reference', Applicant: 'applicant', Status: 'status', Due: 'dueAt' } as const;

/**
 * The work queue pattern: queue tabs with counts, a status filter, due (SLA) tags against the pinned showcase clock,
 * bulk selection with a bulk action and a row action. The state switch shows loading, empty and error.
 */
@Component({
  selector: 'app-showcase-work-queue-page',
  imports: [
    DgaButtonComponent,
    DgaSelectComponent,
    DgaStatusTagComponent,
    ListCellDirective,
    ListRowActionsDirective,
    ShowcaseFrameComponent,
    ShowcaseStateSwitchComponent,
    TranslatePipe,
    WorkQueuePatternComponent,
  ],
  templateUrl: './work-queue-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkQueuePageComponent {
  private readonly translations = inject(TranslationService);
  private readonly clock = inject(APP_CLOCK);
  private readonly router = inject(Router);

  protected readonly pageId = ShowcasePageId.WorkQueue;
  protected readonly keys = ShowcaseTranslationKeys;
  protected readonly Column = QueueColumn;
  protected readonly tabs = queueTabs();
  protected readonly columns: readonly ColumnDef<QueueRow>[] = [
    { key: QueueColumn.Reference, headerKey: P.ColumnReference, sortable: true },
    { key: QueueColumn.Applicant, headerKey: P.ColumnApplicant },
    { key: QueueColumn.Status, headerKey: P.ColumnStatus },
  ];
  protected readonly dueColumn: DueColumnDef<QueueRow> = {
    key: QueueColumn.Due,
    headerKey: P.ColumnDue,
    dueAt: row => row.dueAt,
    sortable: true,
  };
  protected readonly rowId = (row: QueueRow) => row.id;
  protected readonly rowLabel = (row: QueueRow) => row.reference;

  readonly state = signal<ShowcaseState>(ShowcaseState.Ready);
  readonly tab = signal<string>(QueueView.Mine);
  readonly page = signal(1);
  readonly search = signal('');
  readonly sort = signal<SortState | null>(null);
  readonly selected = signal<ReadonlySet<QueueRowId>>(NO_SELECTION);
  readonly status = signal<QueueStatus | null>(null);

  protected readonly statusOptions = computed<DgaSelectOption<QueueStatus>[]>(() =>
    Object.values(QueueStatus).map(value => ({
      value,
      label: this.translations.translate(QUEUE_STATUS_DISPLAY[value].labelKey),
    })),
  );

  protected readonly view = computed(() => {
    const rows = applyListQuery(
      queueRows(this.tab(), this.status(), this.clock),
      { search: this.search(), sort: this.sort() },
      {
        textOf: row => `${row.reference} ${this.translations.translate(row.applicantKey)}`,
        sortValue: (row, key) => (key === QueueColumn.Due ? (row.dueAt ?? '') : row.reference),
      },
    );
    return showcaseListView(this.state(), rows);
  });

  /** How a status reads in the status column: its label key and tag tone. */
  protected display(status: QueueStatus): (typeof QUEUE_STATUS_DISPLAY)[QueueStatus] {
    return QUEUE_STATUS_DISPLAY[status];
  }

  retry(): void {
    this.state.set(ShowcaseState.Ready);
  }

  /** Opens the staff-detail demo, the page a queue row leads to. */
  open(): Promise<boolean> {
    return this.router.navigateByUrl(showcaseLink(ShowcasePageId.StaffDetail));
  }

  clearSelection(): void {
    this.selected.set(NO_SELECTION);
  }
}
