import { Injectable, inject } from '@angular/core';
import type { SigningSessionBlock } from '@codelab/ng-security';
import type { Observable } from 'rxjs';
import { ApiService } from '../api.service';
import { selfHandledContext } from '../../http/http-context';
import { ApiPaths } from './api-paths';
import type { LoginRequest, SessionResponse } from './api.models';

/**
 * The auth endpoints. Login and refresh are anonymous; all three handle their own failures (the login
 * form shows a login refusal inline, and a failed refresh or logout ends the session quietly).
 */
@Injectable({ providedIn: 'root' })
export class AuthClient {
  private readonly api = inject(ApiService);

  login(request: LoginRequest): Observable<SessionResponse> {
    return this.api.post<SessionResponse>(ApiPaths.AuthLogin, request, {
      anonymous: true,
      context: selfHandledContext(),
    });
  }

  refresh(refreshToken: string): Observable<SessionResponse> {
    return this.api.post<SessionResponse>(ApiPaths.AuthRefresh, { refreshToken }, {
      anonymous: true,
      context: selfHandledContext(),
    });
  }

  /** The request-signing block of the current session (404 when the server does not sign). */
  signingKey(): Observable<SigningSessionBlock> {
    return this.api.get<SigningSessionBlock>(ApiPaths.SessionSigningKey, { context: selfHandledContext() });
  }

  logout(): Observable<void> {
    return this.api.post<void>(ApiPaths.AuthLogout, null, { context: selfHandledContext() });
  }
}
