import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DgaButtonComponent, DgaStatusTagComponent } from '@codelab/ng-dga';
import { LanguageService } from '../../../core/i18n/language.service';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import {
  AdminListPatternComponent,
  EditorDialogPatternComponent,
  ListCellDirective,
  ListRowActionsDirective,
  type ColumnDef,
} from '../../../patterns';
import { HasPermissionDirective } from '../../../shared/permissions/has-permission.directive';
import { ADMIN_TRANSLATED_CODES } from '../organizations/data/admin-api-error';
import { formatInstant } from '../users/activation-link/format-instant';
import { jobName, runStatusLabelKey, runStatusTone } from './job-labels';
import type { JobSummary } from './job.models';
import { JobsPageStore } from './jobs-page.store';
import { JobsPermissionKeys } from './jobs-permissions';
import { JobsTranslationKeys } from './jobs-translation-keys';
import { JobsPaths } from './jobs.paths';

/**
 * The background jobs screen (`/admin/jobs`, `jobs.view`): the job catalog on the admin-list pattern with each job's
 * schedule and last run (a status tag), a link to its run history, and "Run now" (`jobs.run`) behind a confirmation.
 * A started run opens the job's run history, which follows it until it finishes. The component renders the store's
 * state and forwards intent.
 */
@Component({
  selector: 'app-jobs-page',
  imports: [
    AdminListPatternComponent,
    DgaButtonComponent,
    DgaStatusTagComponent,
    EditorDialogPatternComponent,
    HasPermissionDirective,
    ListCellDirective,
    ListRowActionsDirective,
    RouterLink,
    TranslatePipe,
  ],
  providers: [JobsPageStore],
  templateUrl: './jobs-page.component.html',
  styleUrl: './jobs-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobsPageComponent {
  private readonly language = inject(LanguageService);
  private readonly router = inject(Router);
  protected readonly store = inject(JobsPageStore);
  protected readonly keys = JobsTranslationKeys;
  protected readonly translatedCodes = ADMIN_TRANSLATED_CODES;
  protected readonly permissions = JobsPermissionKeys;
  protected readonly paths = JobsPaths;
  protected readonly statusLabelKey = runStatusLabelKey;
  protected readonly statusTone = runStatusTone;

  protected readonly columns: readonly ColumnDef<JobSummary>[] = [
    { key: 'nameEn', headerKey: JobsTranslationKeys.Columns.Name },
    { key: 'cron', headerKey: JobsTranslationKeys.Columns.Schedule },
    { key: 'lastRun', headerKey: JobsTranslationKeys.Columns.LastRun },
  ];
  protected readonly rowId = (row: JobSummary): string => row.key;

  constructor() {
    void this.store.refresh();
  }

  protected name(job: JobSummary): string {
    return jobName(job, this.language.current());
  }

  protected instant(iso: string): string {
    return formatInstant(iso, this.language.current());
  }

  /** Confirms the pending run; a started run opens that job's run history. */
  protected async confirmRun(): Promise<void> {
    const jobKey = await this.store.confirmRun();
    if (jobKey !== null) {
      await this.router.navigate(JobsPaths.runs(jobKey));
    }
  }
}
