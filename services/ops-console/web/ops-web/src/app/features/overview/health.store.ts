import { DestroyRef, Injectable, computed, inject } from '@angular/core';
import { OpsApiClient } from '../../core/api/ops-api.client';
import type { OpsHealthCheck, OpsHealthReport } from '../../core/api/ops-api.models';
import { RemoteResource } from '../../core/state/remote-resource';
import { byWorstFirst, healthStatusView } from './health-status';

const NO_CHECKS: readonly OpsHealthCheck[] = Object.freeze([]);

/** The Overview screen's state: the health report (`/ops-api/health`), checks worst first. Provided by the page. */
@Injectable()
export class HealthStore {
  private readonly client = inject(OpsApiClient);
  private readonly resource = new RemoteResource<OpsHealthReport>();

  readonly loading = this.resource.loading;
  readonly error = this.resource.error;
  readonly report = this.resource.data;
  readonly checks = computed(() => [...(this.report()?.checks ?? NO_CHECKS)].sort(byWorstFirst));
  /** The overall status as tone and label, or null before the first answer. */
  readonly overall = computed(() => {
    const report = this.report();
    return report ? healthStatusView(report.status) : null;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.resource.cancel());
  }

  load(): void {
    this.resource.load(this.client.health());
  }
}
