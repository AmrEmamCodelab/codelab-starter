import { HttpClient, HttpContext, HttpParams, type HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { createApiRootClassifier } from '@codelab/ng-security';
import { throwError, type Observable } from 'rxjs';
import { AppConfigService } from '../config/app-config.service';
import { TokenStore } from '../auth/token.store';
import { UNSIGNED_REQUEST, unsignedSelfHandledContext } from '../http/http-context';

/** The bearer scheme ApiService writes into the Authorization header. */
export const AUTHORIZATION_HEADER = 'Authorization';
export const BEARER_SCHEME = 'Bearer';

/**
 * `uploadMultipart` was handed a URL outside the configured attachment roots (`origins.attachment` in config.json).
 * The upload is refused before anything is sent, so an upload ticket never leaves for a host the deployment does
 * not name. The message never contains the URL.
 */
export class UntrustedUploadUrlError extends Error {
  constructor() {
    super('The upload URL is not under a configured attachment root (origins.attachment).');
    this.name = 'UntrustedUploadUrlError';
  }
}

/** The HTTP method a multipart upload uses. */
const UPLOAD_METHOD = 'POST';

export interface ApiOptions {
  readonly params?: HttpParams;
  /** Extra headers, merged over the ones ApiService sets. */
  readonly headers?: Readonly<Record<string, string>>;
  /** Per-request context for the response-phase interceptors, such as `SKIP_ERROR_DIALOG`. */
  readonly context?: HttpContext;
  /**
   * Send no Authorization header (login, refresh). An anonymous call is also sent unsigned (`UNSIGNED_REQUEST`): the
   * server checks proofs only on authenticated requests and never signs the answer to an anonymous one.
   */
  readonly anonymous?: boolean;
  /**
   * The bearer token to send instead of the stored session's. Only for the signing-key issuance of a session that is
   * not published yet (`AuthService` adopts the keys before it stores the tokens). Ignored for an anonymous call.
   */
  readonly accessToken?: string;
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

  /** Classifies a URL against `origins.attachment`: the path under a matching root, or null when none matches. */
  private readonly attachmentRoots = createApiRootClassifier({
    roots: () => this.config.origins.attachment,
    pageOrigin: () => globalThis.location.origin,
  });

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

  /** GET a file: the body arrives as a `Blob` (a signed call; the server marks such endpoints `[UnsignedResponse]`). */
  getBlob(path: string, options?: ApiOptions): Observable<Blob> {
    return this.http.get(this.url(path), { ...this.requestOptions(options), responseType: 'blob' });
  }

  /**
   * POSTs `formData` (multipart) to `absoluteUrl`, a separate service such as the attachment service, and emits every
   * HTTP event, upload progress included (`HttpEventType.UploadProgress`, then `Response` with the JSON body).
   *
   * The request carries ONLY `headers` (such as an upload ticket): no bearer token, no request signature (it is sent
   * `UNSIGNED_REQUEST`) and no cookies. Its failures are the caller's (no error dialog), and its 401 is the service's
   * answer, never an expired session. The URL must lie under one of `origins.attachment` (origin and path segment);
   * anything else errors with {@link UntrustedUploadUrlError} and nothing is sent.
   */
  uploadMultipart<T>(
    absoluteUrl: string,
    formData: FormData,
    headers: Readonly<Record<string, string>>,
  ): Observable<HttpEvent<T>> {
    if (this.attachmentRoots(absoluteUrl) === null) {
      return throwError(() => new UntrustedUploadUrlError());
    }
    return this.http.request<T>(UPLOAD_METHOD, absoluteUrl, {
      body: formData,
      headers: { ...headers },
      context: unsignedSelfHandledContext(),
      observe: 'events',
      reportProgress: true,
      responseType: 'json',
      withCredentials: false,
    });
  }

  /** The absolute URL a path resolves to. */
  url(path: string): string {
    return this.config.apiUrl(path);
  }

  private requestOptions(options?: ApiOptions) {
    const token = options?.anonymous ? null : (options?.accessToken ?? this.tokens.accessToken());
    return {
      headers: {
        ...(token ? { [AUTHORIZATION_HEADER]: `${BEARER_SCHEME} ${token}` } : {}),
        ...(options?.headers ?? {}),
      },
      params: options?.params,
      context: options?.anonymous ? unsignedCopy(options.context) : options?.context,
    };
  }
}

/** A copy of `context` (the caller's object is left untouched) that also carries `UNSIGNED_REQUEST`. */
function unsignedCopy(context: HttpContext | undefined): HttpContext {
  const copy = new HttpContext();
  for (const token of context?.keys() ?? []) {
    copy.set(token, context?.get(token));
  }
  return copy.set(UNSIGNED_REQUEST, true);
}
