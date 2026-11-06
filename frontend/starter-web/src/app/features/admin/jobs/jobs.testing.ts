import { JobRunStatus, JobTrigger, SYSTEM_ACTOR, type JobRun, type JobRunId, type JobSummary } from './job.models';

/** Fixtures shared by the jobs specs. Spec-only: nothing in the app imports this file. */
export function jobRun(overrides: Partial<JobRun> = {}): JobRun {
  return {
    id: 'run-1' as JobRunId,
    status: JobRunStatus.Succeeded,
    trigger: JobTrigger.Schedule,
    actor: SYSTEM_ACTOR,
    triggeredBy: null,
    startedAt: '2026-11-05T09:00:00Z',
    finishedAt: '2026-11-05T09:00:02Z',
    referenceCode: null,
    ...overrides,
  };
}

export function jobSummary(overrides: Partial<JobSummary> = {}): JobSummary {
  return {
    key: 'outbox-drain',
    nameAr: 'تفريغ صندوق الصادر',
    nameEn: 'Outbox drain',
    cron: '*/5 * * * *',
    lastRun: jobRun(),
    ...overrides,
  };
}

/** The two jobs the backend registers: one with a last run, one that never ran. */
export function catalog(): JobSummary[] {
  return [
    jobSummary(),
    jobSummary({ key: 'security-replay-purge', nameAr: 'تنظيف سجل الإعادة', nameEn: 'Replay purge', cron: '0 3 * * *', lastRun: null }),
  ];
}
