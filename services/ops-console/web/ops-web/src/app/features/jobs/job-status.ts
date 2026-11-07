import type { DgaStatusTone } from '@codelab/ng-dga';
import { JobRunStatus, type OpsJob } from '../../core/api/ops-api.models';

/** Translation keys of the Jobs screen. */
export const JobKeys = {
  Title: 'jobs.title',
  Subtitle: 'jobs.subtitle',
  Caption: 'jobs.caption',
  Empty: 'jobs.empty',
  /** `{{count}}`. */
  FailedSummary: 'jobs.failedSummary',
  AllGood: 'jobs.allGood',
  Columns: {
    Key: 'jobs.columns.key',
    Status: 'jobs.columns.status',
    LastStarted: 'jobs.columns.lastStarted',
    LastFinished: 'jobs.columns.lastFinished',
    NextRun: 'jobs.columns.nextRun',
  },
  Status: {
    Queued: 'jobs.status.queued',
    Running: 'jobs.status.running',
    Succeeded: 'jobs.status.succeeded',
    Failed: 'jobs.status.failed',
    NeverRun: 'jobs.status.neverRun',
    Unknown: 'jobs.status.unknown',
  },
} as const;

/** How a job's last-run status renders. */
export interface JobStatusView {
  readonly tone: DgaStatusTone;
  readonly labelKey: string;
}

const STATUS_VIEWS: ReadonlyMap<JobRunStatus, JobStatusView> = new Map([
  [JobRunStatus.Queued, { tone: 'info', labelKey: JobKeys.Status.Queued }],
  [JobRunStatus.Running, { tone: 'progress', labelKey: JobKeys.Status.Running }],
  [JobRunStatus.Succeeded, { tone: 'success', labelKey: JobKeys.Status.Succeeded }],
  [JobRunStatus.Failed, { tone: 'error', labelKey: JobKeys.Status.Failed }],
]);

const NEVER_RUN_VIEW: JobStatusView = Object.freeze({ tone: 'neutral', labelKey: JobKeys.Status.NeverRun });
const UNKNOWN_STATUS_VIEW: JobStatusView = Object.freeze({ tone: 'neutral', labelKey: JobKeys.Status.Unknown });

/** The tone and label of a job's last run; null is "never run" and an unknown value is "unknown". */
export function jobStatusView(status: JobRunStatus | null): JobStatusView {
  if (status === null) {
    return NEVER_RUN_VIEW;
  }
  return STATUS_VIEWS.get(status) ?? UNKNOWN_STATUS_VIEW;
}

/** Failed jobs first, then by key, so the job that needs a look is the first row. */
export function byFailedFirst(left: OpsJob, right: OpsJob): number {
  const leftFailed = left.lastStatus === JobRunStatus.Failed;
  const rightFailed = right.lastStatus === JobRunStatus.Failed;
  if (leftFailed !== rightFailed) {
    return leftFailed ? -1 : 1;
  }
  return left.key.localeCompare(right.key);
}
