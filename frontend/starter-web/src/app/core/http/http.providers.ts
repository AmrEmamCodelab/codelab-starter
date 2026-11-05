import { provideHttpClient, withInterceptors, type HttpInterceptorFn } from '@angular/common/http';
import type { EnvironmentProviders } from '@angular/core';
import { responseSignatureInterceptor, signatureInterceptor } from '@codelab/ng-security';
import { errorDialogInterceptor } from './error-dialog.interceptor';
import { forbiddenInterceptor } from './forbidden.interceptor';
import { UNSIGNED_REQUEST } from './http-context';
import { sessionExpiredInterceptor } from './session-expired.interceptor';

/**
 * `signatureInterceptor`, except for a request marked {@link UNSIGNED_REQUEST}: that one goes out unsigned, so
 * `responseSignatureInterceptor` (which verifies only signed requests) does not expect the signature the server never
 * writes for it.
 */
export const sessionSignatureInterceptor: HttpInterceptorFn = (request, next) =>
  request.context.get(UNSIGNED_REQUEST) ? next(request) : signatureInterceptor(request, next);

/**
 * The HTTP stack. The app's own interceptors are response-phase only (ApiService owns the URL and
 * auth header); the two ng-security interceptors sign requests and verify responses and must come
 * LAST, in this order.
 */
export function provideStarterHttp(): EnvironmentProviders {
  return provideHttpClient(
    withInterceptors([
      sessionExpiredInterceptor,
      forbiddenInterceptor,
      errorDialogInterceptor,
      sessionSignatureInterceptor,
      responseSignatureInterceptor,
    ]),
  );
}
