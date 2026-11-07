import { ChangeDetectionStrategy, Component, inject, type OnInit } from '@angular/core';
import { DgaButtonComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import type { OpsHealthCheck } from '../../core/api/ops-api.models';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { DateTimePipe } from '../../core/i18n/date-time';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import type { ColumnDef } from '../../patterns/ops-list/list-columns';
import { OpsListCellDirective } from '../../patterns/ops-list/ops-list-cell.directive';
import { OpsListComponent } from '../../patterns/ops-list/ops-list.component';
import { OverviewKeys, healthStatusView } from './health-status';
import { HealthStore } from './health.store';

/** Overview: the overall health and every check, worst first, with a manual refresh. */
@Component({
  selector: 'app-overview-page',
  imports: [DateTimePipe, DgaButtonComponent, DgaStatusTagComponent, OpsListCellDirective, OpsListComponent, TranslatePipe],
  providers: [HealthStore],
  templateUrl: './overview.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverviewPage implements OnInit {
  protected readonly store = inject(HealthStore);
  protected readonly keys = OverviewKeys;
  protected readonly common = CommonKeys;
  protected readonly statusView = healthStatusView;
  protected readonly columns: readonly ColumnDef<OpsHealthCheck>[] = [
    { key: 'name', headerKey: OverviewKeys.Columns.Name, ltr: true },
    { key: 'status', headerKey: OverviewKeys.Columns.Status },
    { key: 'durationMs', headerKey: OverviewKeys.Columns.Duration },
    { key: 'description', headerKey: OverviewKeys.Columns.Description },
  ];
  protected readonly rowId = (check: OpsHealthCheck): string => check.name;

  ngOnInit(): void {
    this.store.load();
  }
}
