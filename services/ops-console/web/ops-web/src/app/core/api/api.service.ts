import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { OPS_CONFIG } from '../config/ops-config';

const PATH_SEPARATOR = '/';
const LEADING_SEPARATORS = /^\/+/;

/**
 * The single path every BFF call takes, and the ONLY `HttpClient` user in the console. Callers pass paths
 * (`OpsApiPaths`), never URLs; this service resolves them against config.json's same-origin `apiBaseUrl`.
 *
 * Authentication is the browser's: the BFF sits behind Windows authentication, so every call is sent with credentials
 * and carries no token and no Authorization header of the console's own. Response handling (401, 403) lives in the
 * response-phase interceptor under core/http.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(OPS_CONFIG);

  get<T>(path: string): Observable<T> {
    return this.http.get<T>(this.url(path), { withCredentials: true });
  }

  /** The same-origin URL a path resolves to (`health` gives `/ops-api/health`). */
  url(path: string): string {
    return `${this.config.apiBaseUrl}${PATH_SEPARATOR}${path.replace(LEADING_SEPARATORS, '')}`;
  }
}
