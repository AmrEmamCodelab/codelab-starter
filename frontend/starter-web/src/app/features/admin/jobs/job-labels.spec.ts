import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { JobTriggerLabelKeys, actorLabelKey, isRunActive, jobName, runStatusLabelKey, runStatusTone } from './job-labels';
import { JobRunStatus, JobTrigger } from './job.models';
import { JobsTranslationKeys } from './jobs-translation-keys';
import { jobRun, jobSummary } from './jobs.testing';

describe('job labels', () => {
  it('labels and tones every status', () => {
    expect(runStatusLabelKey(JobRunStatus.Queued)).toBe(JobsTranslationKeys.Status.Queued);
    expect(runStatusLabelKey(JobRunStatus.Running)).toBe(JobsTranslationKeys.Status.Running);
    expect(runStatusLabelKey(JobRunStatus.Succeeded)).toBe(JobsTranslationKeys.Status.Succeeded);
    expect(runStatusLabelKey(JobRunStatus.Failed)).toBe(JobsTranslationKeys.Status.Failed);
    expect([JobRunStatus.Queued, JobRunStatus.Running, JobRunStatus.Succeeded, JobRunStatus.Failed].map(runStatusTone)).toEqual([
      'info',
      'progress',
      'success',
      'error',
    ]);
  });

  it('falls back for a status a newer server added', () => {
    const unknown = 99 as JobRunStatus;
    expect(runStatusTone(unknown)).toBe('neutral');
    expect(runStatusLabelKey(unknown)).toBe(JobsTranslationKeys.Status.Queued);
  });

  it('labels both triggers', () => {
    expect(JobTriggerLabelKeys[JobTrigger.Schedule]).toBe(JobsTranslationKeys.Triggers.Schedule);
    expect(JobTriggerLabelKeys[JobTrigger.Manual]).toBe(JobsTranslationKeys.Triggers.Manual);
  });

  it('treats queued and running runs as active', () => {
    expect(isRunActive(jobRun({ status: JobRunStatus.Queued }))).toBe(true);
    expect(isRunActive(jobRun({ status: JobRunStatus.Running }))).toBe(true);
    expect(isRunActive(jobRun({ status: JobRunStatus.Succeeded }))).toBe(false);
    expect(isRunActive(jobRun({ status: JobRunStatus.Failed }))).toBe(false);
  });

  it('names the job in the language and labels the system actor', () => {
    expect(jobName(jobSummary(), AppLanguages.Arabic)).toBe('تفريغ صندوق الصادر');
    expect(jobName(jobSummary(), AppLanguages.English)).toBe('Outbox drain');
    expect(actorLabelKey(jobRun())).toBe(JobsTranslationKeys.Runs.ActorSystem);
    expect(actorLabelKey(jobRun({ actor: 'other' }))).toBeNull();
  });
});
