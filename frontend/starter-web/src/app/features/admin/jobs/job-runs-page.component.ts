import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  FIRST_PAGE,
  ListCellDirective,
  type ColumnDef,
} from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { ADMIN_TRANSLATED_CODES } from '../organizations/data/admin-api-error';
import { formatInstant } from '../users/activation-link/format-instant';
import { JOB_RUNS_TAKE } from './admin-jobs.client';
import { JobTriggerLabelKeys, actorLabelKey, jobName, runStatusLabelKey, runStatusTone } from './job-labels';
import type { JobRun } from './job.models';
import { JobRunsPageStore } from './job-runs-page.store';
import { JobsPermissionKeys } from './jobs-permissions';
import { JobsTranslationKeys } from './jobs-translation-keys';
import { JobsPaths } from './jobs.paths';

/**
 * A job's run history (`/admin/jobs/:jobKey/runs`, `jobs.view`): its latest runs, newest first, with status, trigger,
 * actor (always the system account), who triggered a manual run, start and finish times and a failed run's reference
 * code; Refresh, and "Run now" (`jobs.run`) behind a confirmation. Unfinished runs refresh by themselves. `jobKey`
 * is bound from the route parameter.
 */
@Component({
  selector: 'app-job-runs-page',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaStatusTagComponent,
    EditorDialogPatternComponent,
    HasPermissionDirective,
    ListCellDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [JobRunsPageStore],
  templateUrl: './job-runs-page.component.html',
  styleUrl: './job-runs-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobRunsPageComponent {
  /** The job's key, from the route parameter `JobsRouteParams.JobKey`. */
  readonly jobKey = input.required<string>();

  private readonly language = inject(LanguageService);
  protected readonly store = inject(JobRunsPageStore);
  protected readonly keys = JobsTranslationKeys;
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly permissions = JobsPermissionKeys;
  protected readonly paths = JobsPaths;
  protected readonly triggerLabelKeys = JobTriggerLabelKeys;
  protected readonly statusLabelKey = runStatusLabelKey;
  protected readonly statusTone = runStatusTone;
  protected readonly actorKey = actorLabelKey;
  /** The history is one page: the server answers at most {@link JOB_RUNS_TAKE} runs. */
  protected readonly pageSize = JOB_RUNS_TAKE;
  protected readonly firstPage = FIRST_PAGE;

  protected readonly columns: readonly ColumnDef<JobRun>[] = [
    { key: 'status', headerKey: JobsTranslationKeys.Runs.Columns.Status },
    { key: 'trigger', headerKey: JobsTranslationKeys.Runs.Columns.Trigger },
    { key: 'actor', headerKey: JobsTranslationKeys.Runs.Columns.Actor },
    { key: 'triggeredBy', headerKey: JobsTranslationKeys.Runs.Columns.TriggeredBy },
    { key: 'startedAt', headerKey: JobsTranslationKeys.Runs.Columns.StartedAt },
    { key: 'finishedAt', headerKey: JobsTranslationKeys.Runs.Columns.FinishedAt },
    { key: 'referenceCode', headerKey: JobsTranslationKeys.Runs.Columns.ReferenceCode },
  ];
  protected readonly rowId = (row: JobRun): string => row.id;
  /** The job's name in the current language, or its key until the catalog answers. */
  protected readonly title = computed(() => {
    const job = this.store.job();
    return job === null ? this.jobKey() : jobName(job, this.language.current());
  });

  constructor() {
    effect(() => {
      const jobKey = this.jobKey();
      untracked(() => void this.store.open(jobKey));
    });
  }

  protected instant(iso: string): string {
    return formatInstant(iso, this.language.current());
  }
}
