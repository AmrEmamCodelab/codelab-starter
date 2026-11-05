import { provideHttpClient, withInterceptors } from '@angular/common/http';
import type { EnvironmentProviders } from '@angular/core';
import { responseSignatureInterceptor, signatureInterceptor } from '@codelab/ng-security';
import { errorDialogInterceptor } from './error-dialog.interceptor';
import { forbiddenInterceptor } from './forbidden.interceptor';
import { sessionExpiredInterceptor } from './session-expired.interceptor';

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
      signatureInterceptor,
      responseSignatureInterceptor,
    ]),
  );
}
