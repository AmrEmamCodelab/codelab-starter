/**
 * What the request and response vector specs share: the case's session keys (derived from the
 * fixture master key, as the server derives the key it hands out at login), its body-hash field and
 * the verdict vocabulary. Spec support only: excluded from the build.
 */

import type { SecurityProtocol } from '../protocol/security-protocol.ts';
import type { SessionSigningKeys } from '../session/session-keys.store.ts';
import { UNSIGNED_BODY, hashBody } from '../signing/signature-canonical.ts';
import { bodyBytes, hkdf, hmacKeys, sessionKeyInfo, type VectorBody } from './vector-crypto.ts';
import { flag, ringKey, text, type FixtureRing, type VectorCase } from './vector-files.ts';

/** The verdicts a verify case pins (the same words the C# runner produces). */
export const Verdicts = Object.freeze({ valid: 'valid', malformed: 'malformed', mismatch: 'mismatch' } as const);

export type Verdict = (typeof Verdicts)[keyof typeof Verdicts];

export const CaseKinds = Object.freeze({ sign: 'sign', verify: 'verify' } as const);

/** The audience the session keys carry when a case does not name one (response cases). */
const NO_AUDIENCE = '';

/** The case's body as the file describes it. */
export function caseBody(vectorCase: VectorCase): VectorBody {
  return vectorCase['body'] as VectorBody;
}

/** The body-hash field: the exemption marker, or the digest of the exact bytes. */
export function caseBodyHash(vectorCase: VectorCase): Promise<string> {
  return flag(vectorCase, 'bodyUnsigned') ? Promise.resolve(UNSIGNED_BODY) : hashBody(bodyBytes(caseBody(vectorCase)));
}

/** The session keys of the case's master key, user and session, imported like the session store imports them. */
export async function caseSessionKeys(
  protocol: SecurityProtocol,
  ring: FixtureRing,
  vectorCase: VectorCase,
): Promise<SessionSigningKeys> {
  const info = sessionKeyInfo(protocol, { userId: text(vectorCase, 'userId'), sessionId: text(vectorCase, 'sessionId') });
  const key = await hkdf(ringKey(ring, text(vectorCase, 'masterKeyId')), info);
  const audience = typeof vectorCase['audience'] === 'string' ? vectorCase['audience'] : NO_AUDIENCE;
  return { ...(await hmacKeys(key)), audience, verifyResponses: true };
}
