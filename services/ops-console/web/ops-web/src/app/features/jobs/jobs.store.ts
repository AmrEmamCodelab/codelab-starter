import { DestroyRef, Injectable, computed, inject } from '@angular/core';
import { OpsApiClient } from '../../core/api/ops-api.client';
import { JobRunStatus, type OpsJob } from '../../core/api/ops-api.models';
import { RemoteResource } from '../../core/state/remote-resource';
import { byFailedFirst } from './job-status';

const NO_JOBS: readonly OpsJob[] = Object.freeze([]);

/** The Jobs screen's state: every recurring job and its last run (`/ops-api/jobs`), failed first. Provided by the page. */
@Injectable()
export class JobsStore {
  private readonly client = inject(OpsApiClient);
  private readonly resource = new RemoteResource<readonly OpsJob[]>();

  readonly loading = this.resource.loading;
  readonly error = this.resource.error;
  readonly loaded = computed(() => this.resource.data() !== null);
  readonly rows = computed(() => [...(this.resource.data() ?? NO_JOBS)].sort(byFailedFirst));
  readonly failedCount = computed(() => this.rows().filter((job) => job.lastStatus === JobRunStatus.Failed).length);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resource.cancel());
  }

  load(): void {
    this.resource.load(this.client.jobs());
  }
}
