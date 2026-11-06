import type { DgaStatusTone } from '@codelab/ng-dga';
import { AppLanguages, type AppLanguage } from '../../../core/i18n/language';
import { JobRunStatus, JobTrigger, SYSTEM_ACTOR, type JobRun, type JobSummary } from './job.models';
import { JobsTranslationKeys } from './jobs-translation-keys';

/** The label of each run status. */
export const JobRunStatusLabelKeys: Readonly<Record<JobRunStatus, string>> = {
  [JobRunStatus.Queued]: JobsTranslationKeys.Status.Queued,
  [JobRunStatus.Running]: JobsTranslationKeys.Status.Running,
  [JobRunStatus.Succeeded]: JobsTranslationKeys.Status.Succeeded,
  [JobRunStatus.Failed]: JobsTranslationKeys.Status.Failed,
};

/** The status tag tone of each run status. */
export const JobRunStatusTones: Readonly<Record<JobRunStatus, DgaStatusTone>> = {
  [JobRunStatus.Queued]: 'info',
  [JobRunStatus.Running]: 'progress',
  [JobRunStatus.Succeeded]: 'success',
  [JobRunStatus.Failed]: 'error',
};

/** The label of each trigger. */
export const JobTriggerLabelKeys: Readonly<Record<JobTrigger, string>> = {
  [JobTrigger.Schedule]: JobsTranslationKeys.Triggers.Schedule,
  [JobTrigger.Manual]: JobsTranslationKeys.Triggers.Manual,
};

const ACTIVE_STATUSES: ReadonlySet<JobRunStatus> = new Set([JobRunStatus.Queued, JobRunStatus.Running]);

/** Whether a run has not finished yet (queued or running), so its row will still change. */
export function isRunActive(run: JobRun): boolean {
  return ACTIVE_STATUSES.has(run.status);
}

/** The label key of a status the client may not know yet (a newer server): unknown shows the generic text. */
export function runStatusLabelKey(status: JobRunStatus): string {
  return JobRunStatusLabelKeys[status] ?? JobsTranslationKeys.Status.Queued;
}

/** The tag tone of a status; an unknown one is neutral. */
export function runStatusTone(status: JobRunStatus): DgaStatusTone {
  return JobRunStatusTones[status] ?? 'neutral';
}

/** The job's name in `language`. */
export function jobName(job: JobSummary, language: AppLanguage): string {
  return language === AppLanguages.Arabic ? job.nameAr : job.nameEn;
}

/** The actor cell: the system actor's label key, or null to show `run.actor` as it is. */
export function actorLabelKey(run: JobRun): string | null {
  return run.actor === SYSTEM_ACTOR ? JobsTranslationKeys.Runs.ActorSystem : null;
}
