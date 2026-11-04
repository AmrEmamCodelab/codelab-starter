/**
 * One call wires the package into an application:
 *
 * ```ts
 * providers: [
 *   provideCodelabSecurity({
 *     prefix: config.securityPrefix,
 *     classifyServiceUrl: createApiRootClassifier({ roots: () => [config.apiUrl], pageOrigin: () => location.origin }),
 *   }),
 *   provideHttpClient(withInterceptors([authInterceptor, signatureInterceptor, responseSignatureInterceptor])),
 * ]
 * ```
 *
 * The two security interceptors go LAST, in that order (see each file for why).
 */

import { inject, type Provider } from '@angular/core';
import { createSecurityProtocol } from '../protocol/security-protocol.ts';
import { SessionKeyStore } from '../session/session-keys.store.ts';
import {
  DEFAULT_SESSION_PERSISTENCE_NAMES,
  indexedDbKeyRecordStore,
  localStorageMarker,
  type SessionPersistenceNames,
} from '../session/session-persistence.ts';
import type { ServiceUrlClassifier } from '../signing/service-url.ts';
import {
  SECURITY_CLOCK,
  SECURITY_PROTOCOL,
  SESSION_VERIFICATION_KEY_SOURCE,
  SIGNED_SERVICE_URL_CLASSIFIER,
  SYSTEM_CLOCK,
  responseVerificationKeySource,
} from './tokens.ts';

/** What an application supplies. */
export interface CodelabSecurityOptions {
  /** The product prefix, the same value the API's security options carry. */
  readonly prefix: string;
  /** Which calls are to our API, and the path the API sees for them. */
  readonly classifyServiceUrl: ServiceUrlClassifier;
  /** Storage names; override only when two starter apps share one origin. */
  readonly persistence?: SessionPersistenceNames;
}

/**
 * The providers for the protocol, the URL classifier, the session key store and the response
 * verification key source.
 *
 * @throws InvalidProtocolPrefixError at bootstrap when the prefix is malformed.
 */
export function provideCodelabSecurity(options: CodelabSecurityOptions): Provider[] {
  const protocol = createSecurityProtocol(options.prefix);
  const names = options.persistence ?? DEFAULT_SESSION_PERSISTENCE_NAMES;
  return [
    { provide: SECURITY_PROTOCOL, useValue: protocol },
    { provide: SIGNED_SERVICE_URL_CLASSIFIER, useValue: options.classifyServiceUrl },
    {
      provide: SessionKeyStore,
      useFactory: () =>
        new SessionKeyStore({
          marker: localStorageMarker(names.markerKey),
          records: indexedDbKeyRecordStore(names),
          clock: inject(SECURITY_CLOCK, { optional: true }) ?? SYSTEM_CLOCK,
        }),
    },
    {
      provide: SESSION_VERIFICATION_KEY_SOURCE,
      useFactory: () => {
        const store = inject(SessionKeyStore);
        return responseVerificationKeySource(() => store.load());
      },
    },
  ];
}
