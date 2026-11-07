import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { catchError, throwError } from 'rxjs';
import { RoutePaths, type RoutePath } from '../routing/route-paths';

/** The access page each refusal status leads to. */
const ACCESS_PAGES: ReadonlyMap<number, RoutePath> = new Map<number, RoutePath>([
  [HttpStatus.unauthorized, RoutePaths.SignInRequired],
  [HttpStatus.forbidden, RoutePaths.AccessDenied],
]);

/** The access page for a failed call, or null when the failure is the screen's to show. */
export function accessPageFor(error: unknown): RoutePath | null {
  return error instanceof HttpErrorResponse ? (ACCESS_PAGES.get(error.status) ?? null) : null;
}

/**
 * Response phase only: a 401 (no Windows identity reached the BFF) or 403 (the identity is not in the operator group)
 * moves the operator to the matching access page. The error still reaches the caller, so no screen shows stale data
 * as if the call had worked.
 */
export const accessInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  return next(request).pipe(
    catchError((error: unknown) => {
      const page = accessPageFor(error);
      if (page !== null) {
        void router.navigate([page]);
      }
      return throwError(() => error);
    }),
  );
};
