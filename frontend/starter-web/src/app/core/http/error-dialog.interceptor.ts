import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { HttpStatus, parseApiError } from '@codelab/ng-security';
import { catchError, throwError } from 'rxjs';
import { SKIP_ERROR_DIALOG } from './http-context';
import { ErrorDialogStore } from './error-dialog.store';

/** Statuses another interceptor answers: 401 ends the session, 403 opens the 403 page. */
const HANDLED_ELSEWHERE: ReadonlySet<number> = new Set([HttpStatus.unauthorized, HttpStatus.forbidden]);

/**
 * Response phase only: an API failure the caller did not opt out of (`SKIP_ERROR_DIALOG`) is parsed
 * with the ng-security problem+json parser and shown in the error dialog. The error is always rethrown,
 * so the caller still sees it.
 */
export const errorDialogInterceptor: HttpInterceptorFn = (request, next) => {
  const dialog = inject(ErrorDialogStore);
  return next(request).pipe(
    catchError((error: unknown) => {
      const shows =
        error instanceof HttpErrorResponse &&
        !request.context.get(SKIP_ERROR_DIALOG) &&
        !HANDLED_ELSEWHERE.has(error.status);
      if (shows) {
        dialog.show(parseApiError(error));
      }
      return throwError(() => error);
    }),
  );
};
