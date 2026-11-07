import { ChangeDetectionStrategy, Component, inject, type OnInit } from '@angular/core';
import { DgaAlertComponent, DgaButtonComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import type { OpsJob } from '../../core/api/ops-api.models';
import { CommonKeys } from '../../core/i18n/common-translation-keys';
import { DateTimePipe } from '../../core/i18n/date-time';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import type { ColumnDef } from '../../patterns/ops-list/list-columns';
import { OpsListCellDirective } from '../../patterns/ops-list/ops-list-cell.directive';
import { OpsListComponent } from '../../patterns/ops-list/ops-list.component';
import { JobKeys, jobStatusView } from './job-status';
import { JobsStore } from './jobs.store';

/** Jobs: every recurring background job with its last run and next run, failed jobs first. */
@Component({
  selector: 'app-jobs-page',
  imports: [DateTimePipe, DgaAlertComponent, DgaButtonComponent, DgaStatusTagComponent, OpsListCellDirective, OpsListComponent, TranslatePipe],
  providers: [JobsStore],
  templateUrl: './jobs.page.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobsPage implements OnInit {
  protected readonly store = inject(JobsStore);
  protected readonly keys = JobKeys;
  protected readonly common = CommonKeys;
  protected readonly statusView = jobStatusView;
  protected readonly columns: readonly ColumnDef<OpsJob>[] = [
    { key: 'key', headerKey: JobKeys.Columns.Key, ltr: true },
    { key: 'lastStatus', headerKey: JobKeys.Columns.Status },
    { key: 'lastStartedAt', headerKey: JobKeys.Columns.LastStarted },
    { key: 'lastFinishedAt', headerKey: JobKeys.Columns.LastFinished },
    { key: 'nextRunAt', headerKey: JobKeys.Columns.NextRun },
  ];
  protected readonly rowId = (job: OpsJob): string => job.key;

  ngOnInit(): void {
    this.store.load();
  }
}
