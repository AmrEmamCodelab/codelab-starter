import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../api.service';
import { ApiPaths } from './api-paths';
import type { EmptyResponse } from './api.models';

/** The dashboard endpoint (requires `dashboard.view`). */
@Injectable({ providedIn: 'root' })
export class DashboardClient {
  private readonly api = inject(ApiService);

  get(): Observable<EmptyResponse> {
    return this.api.get<EmptyResponse>(ApiPaths.Dashboard);
  }
}
