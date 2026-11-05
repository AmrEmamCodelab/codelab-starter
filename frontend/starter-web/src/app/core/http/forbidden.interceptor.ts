import { HttpErrorResponse, type HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { catchError, throwError } from 'rxjs';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { unauthorizedTree } from '../routing/route-trees';
import { SKIP_ERROR_DIALOG } from './http-context';

/**
 * Response phase only: a 403 means the user's permissions changed under them (a revoke takes effect
 * without a new login). The permissions are re-synced, so the menus drop what is gone, and the user
 * lands on the 403 page. A caller that handles its own failures (`SKIP_ERROR_DIALOG`) is left alone.
 * The error is always rethrown.
 */
export const forbiddenInterceptor: HttpInterceptorFn = (request, next) => {
  const router = inject(Router);
  const permissions = inject(PermissionSyncService);
  return next(request).pipe(
    catchError((error: unknown) => {
      const forbidden =
        error instanceof HttpErrorResponse &&
        error.status === HttpStatus.forbidden &&
        !request.context.get(SKIP_ERROR_DIALOG);
      if (forbidden) {
        permissions.refresh().catch(() => undefined);
        void router.navigateByUrl(unauthorizedTree(router));
      }
      return throwError(() => error);
    }),
  );
};
