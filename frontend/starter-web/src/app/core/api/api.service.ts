import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { AppConfigService } from '../config/app-config.service';
import { TokenStore } from '../auth/token.store';

/** The bearer scheme ApiService writes into the Authorization header. */
export const AUTHORIZATION_HEADER = 'Authorization';
export const BEARER_SCHEME = 'Bearer';

export interface ApiOptions {
  readonly params?: HttpParams;
  /** Extra headers, merged over the ones ApiService sets. */
  readonly headers?: Readonly<Record<string, string>>;
  /** Per-request context for the response-phase interceptors, such as `SKIP_ERROR_DIALOG`. */
  readonly context?: HttpContext;
  /** Send no Authorization header (login, refresh). */
  readonly anonymous?: boolean;
}

/**
 * The single path every API call takes, and the ONLY `HttpClient` user in the app: it resolves a path
 * (`api/me/permissions`, from `ApiPaths`) against the runtime API root and attaches the bearer token.
 * Callers pass paths, never URLs, so no service knows where an environment lives.
 *
 * It depends on `TokenStore`, never on `AuthService`: AuthService calls the API through here, so the
 * reverse dependency would be circular. Response handling (401, 403, error dialog) lives in the
 * response-phase interceptors under core/http; request signing is the `@codelab/ng-security`
 * interceptors' job.
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfigService);
  private readonly tokens = inject(TokenStore);

  get<T>(path: string, options?: ApiOptions): Observable<T> {
    return this.http.get<T>(this.url(path), this.requestOptions(options));
  }

  post<T>(path: string, body: unknown, options?: ApiOptions): Observable<T> {
    return this.http.post<T>(this.url(path), body, this.requestOptions(options));
  }

  put<T>(path: string, body: unknown, options?: ApiOptions): Observable<T> {
    return this.http.put<T>(this.url(path), body, this.requestOptions(options));
  }

  delete<T>(path: string, options?: ApiOptions): Observable<T> {
    return this.http.delete<T>(this.url(path), this.requestOptions(options));
  }

  /** The absolute URL a path resolves to. */
  url(path: string): string {
    return this.config.apiUrl(path);
  }

  private requestOptions(options?: ApiOptions) {
    const token = options?.anonymous ? null : this.tokens.accessToken();
    return {
      headers: {
        ...(token ? { [AUTHORIZATION_HEADER]: `${BEARER_SCHEME} ${token}` } : {}),
        ...(options?.headers ?? {}),
      },
      params: options?.params,
      context: options?.context,
    };
  }
}
