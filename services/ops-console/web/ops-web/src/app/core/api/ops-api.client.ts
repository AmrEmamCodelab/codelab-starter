import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from './api.service';
import type { OperatorProfile, OpsCertificate, OpsHealthReport, OpsJob, ReferenceTrace } from './ops-api.models';

/** The BFF paths, relative to `apiBaseUrl`. */
export const OpsApiPaths = {
  Me: 'me',
  Health: 'health',
  Jobs: 'jobs',
  Certificates: 'certificates',
  ReferenceCodes: 'reference-codes',
} as const;

/** The typed BFF client: one method per route, every call through `ApiService`. */
@Injectable({ providedIn: 'root' })
export class OpsApiClient {
  private readonly api = inject(ApiService);

  me(): Observable<OperatorProfile> {
    return this.api.get<OperatorProfile>(OpsApiPaths.Me);
  }

  health(): Observable<OpsHealthReport> {
    return this.api.get<OpsHealthReport>(OpsApiPaths.Health);
  }

  jobs(): Observable<readonly OpsJob[]> {
    return this.api.get<readonly OpsJob[]>(OpsApiPaths.Jobs);
  }

  certificates(): Observable<readonly OpsCertificate[]> {
    return this.api.get<readonly OpsCertificate[]>(OpsApiPaths.Certificates);
  }

  /** The trace of a normalized reference code (`ABCDE-12345`); the code is URL-encoded into the path. */
  referenceCode(code: string): Observable<ReferenceTrace> {
    return this.api.get<ReferenceTrace>(`${OpsApiPaths.ReferenceCodes}/${encodeURIComponent(code)}`);
  }
}
