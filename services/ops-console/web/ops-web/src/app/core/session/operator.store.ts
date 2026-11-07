import { Injectable, computed, inject } from '@angular/core';
import { OpsApiClient } from '../api/ops-api.client';
import type { OperatorProfile } from '../api/ops-api.models';
import { RemoteResource } from '../state/remote-resource';

/**
 * The signed-in operator (`/ops-api/me`). The console never handles a credential: the browser authenticates to the BFF
 * with Windows authentication, and the BFF answers who that is (401 and 403 go to the access pages through the
 * interceptor). Loaded by the shell each time it opens.
 */
@Injectable({ providedIn: 'root' })
export class OperatorStore {
  private readonly client = inject(OpsApiClient);
  private readonly resource = new RemoteResource<OperatorProfile>();

  readonly operator = this.resource.data;
  readonly error = this.resource.error;
  readonly name = computed(() => this.operator()?.name ?? null);

  load(): void {
    this.resource.load(this.client.me());
  }
}
