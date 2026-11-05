import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../api.service';
import { quietContext } from '../../http/http-context';
import { ApiPaths } from './api-paths';
import type { PermissionsResponse } from './api.models';

/** The signed-in user's own data. */
@Injectable({ providedIn: 'root' })
export class MeClient {
  private readonly api = inject(ApiService);

  /** The permission keys the user holds now (resolved live by the server on every call). */
  permissions(): Observable<PermissionsResponse> {
    return this.api.get<PermissionsResponse>(ApiPaths.MePermissions, { context: quietContext() });
  }
}
