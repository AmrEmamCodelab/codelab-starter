import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import { AdminJobsClient } from './admin-jobs.client';
import { isRunActive } from './job-labels';
import { JobRunTrigger } from './job-run-trigger';
import type { JobSummary } from './job.models';
import { RunPoller } from './run-poller';

/**
 * The state of the jobs screen (provided per page): the job catalog with each job's last run, and the confirmed
 * "Run now". While a last run is queued or running the list reloads by itself ({@link RunPoller}).
 */
@Injectable()
export class JobsPageStore {
  private readonly client = inject(AdminJobsClient);
  private readonly poller = new RunPoller(() => void this.load());
  private readonly jobsNow = signal<readonly JobSummary[]>([]);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);
  private sequence = 0;

  readonly jobs = this.jobsNow.asReadonly();
  readonly page = signal(FIRST_PAGE);
  readonly pageSize = DEFAULT_PAGE_SIZE;
  /** The jobs of the current page (the API answers the whole catalog). */
  readonly rows = computed(() => {
    const start = (this.page() - FIRST_PAGE) * this.pageSize;
    return this.jobsNow().slice(start, start + this.pageSize);
  });
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();
  readonly runTrigger = new JobRunTrigger();

  /** A load the user asked for (first view, retry): starts a fresh polling series. */
  refresh(): Promise<void> {
    this.poller.restart();
    return this.load();
  }

  /**
   * Queues the confirmed run and answers the started job's key, for the screen to open its run history (which then
   * follows the new run). Null when nothing was pending or the server refused (the refusal is in `runTrigger.error`).
   */
  async confirmRun(): Promise<string | null> {
    const job = this.runTrigger.pending();
    const accepted = await this.runTrigger.confirm();
    return accepted === null || job === null ? null : job.key;
  }

  private async load(): Promise<void> {
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const jobs = await firstValueFrom(this.client.list());
      if (current === this.sequence) {
        this.jobsNow.set(jobs);
        this.poller.follow(jobs.some((job) => job.lastRun !== null && isRunActive(job.lastRun)));
      }
    } catch (error: unknown) {
      if (current === this.sequence) {
        this.jobsNow.set([]);
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
