import { inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import { AdminJobsClient } from './admin-jobs.client';
import type { JobRunAccepted, JobSummary } from './job.models';

/**
 * The confirmed "Run now" of a job, shared by the jobs list and the run history: {@link ask} opens the confirmation,
 * {@link confirm} queues the run and answers the server's acceptance (null when it was refused, with the refusal in
 * {@link error}, or when nothing was pending). Create it in an injection context (a store's field initializer).
 */
export class JobRunTrigger {
  private readonly client = inject(AdminJobsClient);
  private readonly pendingJob = signal<JobSummary | null>(null);
  private readonly busyNow = signal(false);
  private readonly failure = signal<ApiError | null>(null);

  /** The job waiting for confirmation, or null when the dialog is closed. */
  readonly pending = this.pendingJob.asReadonly();
  readonly busy = this.busyNow.asReadonly();
  readonly error = this.failure.asReadonly();

  ask(job: JobSummary): void {
    this.failure.set(null);
    this.pendingJob.set(job);
  }

  cancel(): void {
    if (!this.busyNow()) {
      this.pendingJob.set(null);
    }
  }

  async confirm(): Promise<JobRunAccepted | null> {
    const job = this.pendingJob();
    if (job === null || this.busyNow()) {
      return null;
    }
    this.busyNow.set(true);
    this.failure.set(null);
    try {
      const accepted = await firstValueFrom(this.client.run(job.key));
      this.pendingJob.set(null);
      return accepted;
    } catch (error: unknown) {
      this.failure.set(apiErrorFrom(error));
      return null;
    } finally {
      this.busyNow.set(false);
    }
  }
}
