import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../api.service';
import { ApiPaths } from './api-paths';
import type { EmptyResponse } from './api.models';

/** The access-administration endpoints (require `access.admin.view`). */
@Injectable({ providedIn: 'root' })
export class AccessClient {
  private readonly api = inject(ApiService);

  admin(): Observable<EmptyResponse> {
    return this.api.get<EmptyResponse>(ApiPaths.AccessAdmin);
  }
}
