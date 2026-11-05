import type { Provider } from '@angular/core';
import { createApiRootClassifier, provideCodelabSecurity } from '@codelab/ng-security';
import type { AppConfig } from '../config/app-config';

/**
 * Wires `@codelab/ng-security` from the runtime configuration: the protocol prefix
 * (`security.protocolPrefix`, equal to the API's `Codelab:Security:Protocol:Prefix`) and the roots whose
 * calls are signed and verified (`origins.api`). Calls anywhere else pass through unsigned.
 *
 * @throws InvalidProtocolPrefixError at bootstrap when the prefix is malformed.
 */
export function provideStarterSecurity(config: AppConfig): Provider[] {
  return provideCodelabSecurity({
    prefix: config.security.protocolPrefix,
    classifyServiceUrl: createApiRootClassifier({
      roots: () => config.origins.api,
      pageOrigin: () => globalThis.location.origin,
    }),
  });
}
