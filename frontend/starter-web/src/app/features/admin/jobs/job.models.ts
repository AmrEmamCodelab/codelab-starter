import type { WireId } from '@codelab/ng-security';

/** The state of a job run, as the backend `JobRunStatus` (integers on the wire). */
export enum JobRunStatus {
  Queued = 1,
  Running = 2,
  Succeeded = 3,
  Failed = 4,
}

/** What started a job run, as the backend `JobTrigger`. */
export enum JobTrigger {
  Schedule = 1,
  Manual = 2,
}

/** The actor every job runs as (the backend `SystemActor.Name`). */
export const SYSTEM_ACTOR = 'SYSTEM';

/** A job run's PublicId: opaque, sent back exactly as received. */
export type JobRunId = WireId<'job-run'>;

/**
 * One run of a job. Instants are ISO 8601 UTC, null until the run starts / finishes. `triggeredBy` is the display name of the
 * administrator who started a manual run (null for a scheduled run or a subject with no account); `referenceCode` is set on a failed run, to quote to support.
 */
export interface JobRun {
  readonly id: JobRunId;
  readonly status: JobRunStatus;
  readonly trigger: JobTrigger;
  readonly actor: string;
  readonly triggeredBy: string | null;
  readonly startedAt: string | null;
  readonly finishedAt: string | null;
  readonly referenceCode: string | null;
}

/** One job of the catalog (`GET api/admin/jobs`); `cron` is null for a job that only runs on demand. */
export interface JobSummary {
  readonly key: string;
  readonly nameAr: string;
  readonly nameEn: string;
  readonly cron: string | null;
  readonly lastRun: JobRun | null;
}

/** The answer of `POST api/admin/jobs/{key}/runs` (202 Accepted). */
export interface JobRunAccepted {
  readonly runId: JobRunId;
}
