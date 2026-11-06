import { Injectable, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import { AdminJobsClient } from './admin-jobs.client';
import { isRunActive } from './job-labels';
import { JobRunTrigger } from './job-run-trigger';
import type { JobRun, JobRunAccepted, JobSummary } from './job.models';
import { RunPoller } from './run-poller';

/**
 * The state of one job's run history (provided per page): the job (from the catalog, for its name and schedule) and
 * its latest runs, newest first, plus the confirmed "Run now". While a run is queued or running the history reloads
 * by itself ({@link RunPoller}); a run the user starts here shows up at once.
 */
@Injectable()
export class JobRunsPageStore {
  private readonly client = inject(AdminJobsClient);
  private readonly poller = new RunPoller(() => void this.load());
  private readonly key = signal<string | null>(null);
  private readonly jobNow = signal<JobSummary | null>(null);
  private readonly runsNow = signal<readonly JobRun[]>([]);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);
  private sequence = 0;

  readonly jobKey = this.key.asReadonly();
  /** The job, or null until loaded (or when the catalog no longer lists it). */
  readonly job = this.jobNow.asReadonly();
  readonly runs = this.runsNow.asReadonly();
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();
  readonly runTrigger = new JobRunTrigger();

  /** Shows the history of `jobKey` (the route parameter). */
  open(jobKey: string): Promise<void> {
    this.key.set(jobKey);
    this.jobNow.set(null);
    this.runsNow.set([]);
    return this.refresh();
  }

  /** A load the user asked for (open, Refresh, retry): starts a fresh polling series. */
  refresh(): Promise<void> {
    this.poller.restart();
    return this.load();
  }

  /** Asks to run this page's job. */
  askRun(): void {
    const job = this.jobNow();
    if (job !== null) {
      this.runTrigger.ask(job);
    }
  }

  /** Queues the confirmed run; on success the history reloads and follows the new run. */
  async confirmRun(): Promise<JobRunAccepted | null> {
    const accepted = await this.runTrigger.confirm();
    if (accepted !== null) {
      await this.refresh();
    }
    return accepted;
  }

  private async load(): Promise<void> {
    const jobKey = this.key();
    if (jobKey === null) {
      return;
    }
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const [jobs, runs] = await Promise.all([firstValueFrom(this.client.list()), firstValueFrom(this.client.runs(jobKey))]);
      if (current === this.sequence) {
        this.jobNow.set(jobs.find((job) => job.key === jobKey) ?? null);
        this.runsNow.set(runs);
        this.poller.follow(runs.some(isRunActive));
      }
    } catch (error: unknown) {
      if (current === this.sequence) {
        this.runsNow.set([]);
        this.failure.set(apiErrorFrom(error));
        this.poller.follow(false);
      }
    } finally {
      if (current === this.sequence) {
        this.busy.set(false);
      }
    }
  }
}
