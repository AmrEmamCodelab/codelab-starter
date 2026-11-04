/**
 * The Angular injection tokens of the package. `provideCodelabSecurity` binds all of them; a test
 * or an unusual host can bind them one by one.
 */

import { InjectionToken } from '@angular/core';
import type { SecurityProtocol } from '../protocol/security-protocol.ts';
import type { ServiceUrlClassifier } from '../signing/service-url.ts';
import type { EpochClock } from '../session/session-keys.store.ts';

/**
 * The protocol names. Injected WITHOUT a default on purpose: a client guessing the prefix produces
 * proofs no server verifies, and the failure would read as a uniform 401.
 */
export const SECURITY_PROTOCOL = new InjectionToken<SecurityProtocol>('Codelab security protocol');

/** Decides which outgoing calls are signed, and the path the API sees for them. */
export const SIGNED_SERVICE_URL_CLASSIFIER = new InjectionToken<ServiceUrlClassifier>(
  'Codelab signed service URL classifier',
);

/**
 * The clock (milliseconds since the Unix epoch) the session store stamps proofs with: the TypeScript
 * counterpart of TimeProvider. Optional; {@link SYSTEM_CLOCK} when not provided.
 */
export const SECURITY_CLOCK = new InjectionToken<EpochClock>('Codelab security clock');

/** The system clock. */
export const SYSTEM_CLOCK: EpochClock = () => Date.now();

/**
 * The narrow port the response verifier needs: the `verify`-only session key handle, or null when
 * responses are not to be checked at all (the session ended, or the deployment does not sign
 * responses). Both nulls are session-wide states set by the server; there is no per-call opt-out.
 *
 * Injected WITHOUT `optional`: a verifier that finds no key source and shrugs is a client that has
 * quietly stopped verifying.
 */
export interface SessionVerificationKeySource {
  responseVerificationKey(): CryptoKey | null | Promise<CryptoKey | null>;
}

export const SESSION_VERIFICATION_KEY_SOURCE = new InjectionToken<SessionVerificationKeySource>(
  'Codelab session response-verification key',
);

/**
 * Adapts a key loader onto {@link SessionVerificationKeySource}, so the `verifyResponses` flag cannot
 * be forgotten at the wiring seam: a rollout with request signing on and response signing off would
 * otherwise demand a header the server is not emitting.
 */
export function responseVerificationKeySource(
  load: () => Promise<{ readonly verify: CryptoKey; readonly verifyResponses: boolean } | null>,
): SessionVerificationKeySource {
  return {
    responseVerificationKey: async () => {
      const keys = await load();
      return keys !== null && keys.verifyResponses ? keys.verify : null;
    },
  };
}
