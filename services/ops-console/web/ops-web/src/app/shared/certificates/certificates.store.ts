import { Injectable, computed, inject } from '@angular/core';
import { OpsApiClient } from '../../core/api/ops-api.client';
import type { OpsCertificate } from '../../core/api/ops-api.models';
import { OPS_CONFIG } from '../../core/config/ops-config';
import { RemoteResource } from '../../core/state/remote-resource';
import { bySoonestExpiry, certificateAlert } from './certificate-status';

const NO_CERTIFICATES: readonly OpsCertificate[] = Object.freeze([]);

/**
 * The certificates the deployment depends on (`/ops-api/certificates`), shared by the Certificates screen and the
 * shell's alert banner so one load serves both. Rows come soonest-expiry first.
 */
@Injectable({ providedIn: 'root' })
export class CertificatesStore {
  private readonly client = inject(OpsApiClient);
  private readonly config = inject(OPS_CONFIG);
  private readonly resource = new RemoteResource<readonly OpsCertificate[]>();

  readonly loading = this.resource.loading;
  readonly error = this.resource.error;
  readonly rows = computed(() => [...(this.resource.data() ?? NO_CERTIFICATES)].sort(bySoonestExpiry));
  /** The banner content, or null (no certificate needs attention, or nothing loaded yet). */
  readonly alert = computed(() => certificateAlert(this.rows(), this.config.idpCertificateName));

  load(): void {
    this.resource.load(this.client.certificates());
  }
}
