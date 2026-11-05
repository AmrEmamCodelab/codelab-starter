import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { SessionEndReasons } from '../routing/route-paths';
import { SKIP_SESSION_EXPIRY } from './http-context';

/**
 * Response phase only: a 401 on a session call means the server no longer accepts the session (it
 * expired, or it was revoked by a logout elsewhere). The session is ended and the user returns to the
 * login page with the current URL remembered. Calls whose 401 is an answer (login, refresh, logout)
 * set `SKIP_SESSION_EXPIRY`. The error is always rethrown.
 */
export const sessionExpiredInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return next(request).pipe(
    catchError((error: unknown) => {
      const expired =
        error instanceof HttpErrorResponse &&
        error.status === HttpStatus.unauthorized &&
        !request.context.get(SKIP_SESSION_EXPIRY) &&
        auth.isAuthenticated();
      if (expired) {
        void auth.expireSession(SessionEndReasons.Expired, router.url);
      }
      return throwError(() => error);
    }),
  );
};
