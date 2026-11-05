import { Injectable, inject } from '@angular/core';
import type { SigningSessionBlock } from '@codelab/ng-security';
import { map, type Observable } from 'rxjs';
import { ApiService } from '../api.service';
import { selfHandledContext, unsignedSelfHandledContext } from '../../http/http-context';
import { ApiPaths, ApiQueryParams } from './api-paths';
import type {
  ActivateRequest,
  AuthModesResponse,
  LoginRequest,
  SessionResponse,
  SsoExchangeRequest,
  SsoLogoutResponse,
} from './api.models';

/**
 * The auth endpoints. Everything that runs outside a session (modes, the logins, the SSO exchange, activation,
 * refresh) is anonymous; every call handles its own failures (the sign-in pages show a refusal inline, and a
 * failed refresh or logout ends the session quietly).
 */
@Injectable({ providedIn: 'root' })
export class AuthClient {
  private readonly api = inject(ApiService);

  /** The sign-in methods the deployment enables. */
  modes(): Observable<AuthModesResponse> {
    return this.api.get<AuthModesResponse>(ApiPaths.AuthModes, { anonymous: true, context: selfHandledContext() });
  }

  login(request: LoginRequest): Observable<SessionResponse> {
    return this.anonymousPost<SessionResponse>(ApiPaths.AuthLogin, request);
  }

  /** Active Directory sign-in: the same request and response as {@link login}. */
  directoryLogin(request: LoginRequest): Observable<SessionResponse> {
    return this.anonymousPost<SessionResponse>(ApiPaths.AuthAdLogin, request);
  }

  /**
   * The absolute URL that starts an SSO sign-in. The browser NAVIGATES there (top level): the API answers a 302
   * to the identity provider and sets its binding cookie on its own origin, which an XHR could not follow.
   *
   * @param returnUrl an app-relative URL the caller already checked with `safeReturnUrl()`, or null.
   * @param binding the base64url SHA-256 of this tab's SSO binding secret (required by the API).
   */
  ssoLoginUrl(returnUrl: string | null, binding: string): string {
    const query = new URLSearchParams();
    if (returnUrl !== null) {
      query.set(ApiQueryParams.ReturnUrl, returnUrl);
    }
    query.set(ApiQueryParams.Binding, binding);
    return `${this.api.url(ApiPaths.AuthSsoLogin)}?${query.toString()}`;
  }

  /** Swaps the one-time SSO callback ticket for a session (the same body as {@link login}). */
  ssoExchange(request: SsoExchangeRequest): Observable<SessionResponse> {
    return this.anonymousPost<SessionResponse>(ApiPaths.AuthSsoExchange, request);
  }

  /** Sets the first password from an activation ticket (204). */
  activate(request: ActivateRequest): Observable<void> {
    return this.anonymousPost<void>(ApiPaths.AuthActivate, request);
  }

  refresh(refreshToken: string): Observable<SessionResponse> {
    return this.anonymousPost<SessionResponse>(ApiPaths.AuthRefresh, { refreshToken });
  }

  /**
   * The request-signing block of the current session (404 when the server does not sign). Sent unsigned: the server
   * exempts key issuance from proofs and answers it unsigned, also after a refresh while the old keys are held.
   */
  signingKey(): Observable<SigningSessionBlock> {
    return this.api.get<SigningSessionBlock>(ApiPaths.SessionSigningKey, { context: unsignedSelfHandledContext() });
  }

  logout(): Observable<void> {
    return this.api.post<void>(ApiPaths.AuthLogout, null, { context: selfHandledContext() });
  }

  /**
   * Ends an SSO session. Resolves to the identity provider's end-session URL, or null when the API answered 204
   * (the session was not an SSO one, or the provider offers no end-session endpoint).
   */
  ssoLogout(): Observable<string | null> {
    return this.api
      .post<SsoLogoutResponse | null>(ApiPaths.AuthSsoLogout, null, { context: selfHandledContext() })
      .pipe(map(body => (typeof body?.endSessionUrl === 'string' ? body.endSessionUrl : null)));
  }

  private anonymousPost<T>(path: string, body: unknown): Observable<T> {
    return this.api.post<T>(path, body, { anonymous: true, context: selfHandledContext() });
  }
}
