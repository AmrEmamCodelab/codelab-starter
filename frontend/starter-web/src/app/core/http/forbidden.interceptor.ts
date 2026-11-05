import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { HttpStatus, isResponseSignatureError } from '@codelab/ng-security';
import { catchError, throwError } from 'rxjs';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { unauthorizedTree } from '../routing/route-trees';
import { SKIP_ERROR_DIALOG } from './http-context';

/**
 * Response phase only: a 403 means the user's permissions changed under them (a revoke takes effect
 * without a new login). The permissions are re-synced, so the menus drop what is gone, and the user
 * lands on the 403 page. A caller that handles its own failures (`SKIP_ERROR_DIALOG`) is left alone.
 * The error is always rethrown.
 *
 * A 403 whose signature could not be verified (`ResponseSignatureError` carrying 403) is treated as a 403 too. Its
 * status is unverified, but acting on it only re-reads the (verified) permissions and leaves the page: it can take
 * access away from the view, never grant it.
 */
export const forbiddenInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const permissions = inject(PermissionSyncService);
  return next(request).pipe(
    catchError((error: unknown) => {
      const forbidden = isForbidden(error) && !request.context.get(SKIP_ERROR_DIALOG);
      if (forbidden) {
        permissions.refresh().catch(() => undefined);
        void router.navigateByUrl(unauthorizedTree(router));
      }
      return throwError(() => error);
    }),
  );
};

/** A 403 answer, verified or not (see {@link forbiddenInterceptor}). */
export function isForbidden(error: unknown): boolean {
  if (error instanceof HttpErrorResponse || isResponseSignatureError(error)) {
    return error.status === HttpStatus.forbidden;
  }
  return false;
}
