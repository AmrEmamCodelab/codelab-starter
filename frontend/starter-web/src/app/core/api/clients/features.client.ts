import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../api.service';
import { selfHandledContext } from '../../http/http-context';
import { ApiPaths } from './api-paths';
import type { FeatureFlagsResponse } from './api.models';

/** The anonymous feature-flag endpoint (flags only, never identities). */
@Injectable({ providedIn: 'root' })
export class FeaturesClient {
  private readonly api = inject(ApiService);

  flags(): Observable<FeatureFlagsResponse> {
    return this.api.get<FeatureFlagsResponse>(ApiPaths.Features, {
      anonymous: true,
      context: selfHandledContext(),
    });
  }
}
