/**
 * `{Prefix}-SIG-v1` canonical forms and digests: the TypeScript twin of Codelab.Security's
 * `SignatureCanonicalForm` and `HmacSignature`.
 *
 * What a signature proves: integrity, anti-replay and anti-CSRF. The server derives the same
 * symmetric key it verifies with, so a valid MAC proves only that the sender held the session key.
 * It is not sender-constraint, not proof-of-possession, not non-repudiation, and it does not
 * authenticate the caller. Nothing built on this file may claim otherwise.
 *
 * Agreement with the server is by shared specification (the golden vectors in `packages/vectors`),
 * not by shared code. HMAC is deterministic, so those vectors pin both halves to identical bytes.
 */

import {
  buildCanonical,
  buildCanonicalBytes,
  canonicalMethod,
  normalizePathForSigning,
  type CryptoBytes,
} from '../canonical/canonical.ts';
import type { SecurityProtocol } from '../protocol/security-protocol.ts';
import { base64UrlEncode } from './base64url.ts';

/**
 * The body-hash value when the bytes are deliberately not covered (a multipart upload on the way
 * out). The server admits it only on an endpoint carrying the exemption attribute, never by content
 * type, and the marker is inside the MAC, so it cannot be grafted onto a normal request.
 */
export const UNSIGNED_BODY = 'UNSIGNED-BODY';

const DIGEST_ALGORITHM = 'SHA-256';
const MAC_ALGORITHM = 'HMAC';

/** The request fields a proof covers, in the order they enter the canonical form. */
export interface SignedRequestFields {
  /** The deployment audience delivered with the session; separates deployments sharing keys. */
  readonly audience: string;
  /** The request method; the allowlist LITERAL enters the form, never the caller's spelling. */
  readonly method: string;
  /**
   * The SERVICE-RELATIVE path: the configured API root removed, no origin. The server signs its
   * `Request.Path` without `PathBase`, so only a real browser against a real deployment can catch a
   * mismatch here. Percent escapes are normalised by {@link buildRequestSigningInput}.
   */
  readonly path: string;
  /** Everything after the first `?` in wire form (`queryOf`), or `''`. */
  readonly query: string;
  /** {@link hashBody} over the exact transmitted bytes (an absent body hashes zero bytes), or {@link UNSIGNED_BODY}. */
  readonly bodyHash: string;
  /** Unix seconds as the ASCII digits that also go on the wire. */
  readonly timestamp: string;
  /** The fresh 22-character base64url nonce. */
  readonly nonce: string;
}

/** The response fields a server signature covers. */
export interface SignedResponseFields {
  /** The nonce of the request this answers, as THIS client sent it. The server never echoes it. */
  readonly requestNonce: string;
  /** The HTTP status, so a signed 200 body cannot be substituted for a 403. */
  readonly status: number;
  /** The exact `Content-Type` value as received, or `''`. */
  readonly contentType: string;
  /** {@link hashBody} over the exact received bytes, or {@link UNSIGNED_BODY}. */
  readonly bodyHash: string;
}

function requestFieldList(protocol: SecurityProtocol, fields: SignedRequestFields): string[] {
  return [
    protocol.signatureVersion,
    fields.audience,
    canonicalMethod(fields.method),
    normalizePathForSigning(fields.path),
    fields.query,
    fields.bodyHash,
    fields.timestamp,
    fields.nonce,
  ];
}

function responseFieldList(protocol: SecurityProtocol, fields: SignedResponseFields): string[] {
  if (!Number.isInteger(fields.status)) {
    throw new TypeError(`${protocol.signatureVersion} response status must be an integer.`);
  }
  // String(200) is '200' in every locale; JavaScript numbers carry no culture.
  return [
    protocol.signatureVersion,
    fields.requestNonce,
    String(fields.status),
    fields.contentType,
    fields.bodyHash,
  ];
}

/**
 * The canonical signing input of a request proof.
 *
 * @throws RangeError when the method is outside the allowlist: the browser chooses its own methods,
 * so a caller reaching this has a bug, not a rejected request.
 */
export function buildRequestSigningInput(protocol: SecurityProtocol, fields: SignedRequestFields): string {
  return buildCanonical(protocol, protocol.requestProfile, requestFieldList(protocol, fields));
}

/** The UTF-8 bytes of {@link buildRequestSigningInput}: what is actually MAC'd. */
export function buildRequestSigningInputBytes(
  protocol: SecurityProtocol,
  fields: SignedRequestFields,
): CryptoBytes {
  return buildCanonicalBytes(protocol, protocol.requestProfile, requestFieldList(protocol, fields));
}

/**
 * The canonical signing input of a response signature.
 *
 * @throws TypeError when the status is not an integer: `'200.5'` or `'NaN'` would build a form the
 * server cannot reconstruct.
 */
export function buildResponseSigningInput(protocol: SecurityProtocol, fields: SignedResponseFields): string {
  return buildCanonical(protocol, protocol.responseProfile, responseFieldList(protocol, fields));
}

/** The UTF-8 bytes of {@link buildResponseSigningInput}. */
export function buildResponseSigningInputBytes(
  protocol: SecurityProtocol,
  fields: SignedResponseFields,
): CryptoBytes {
  return buildCanonicalBytes(protocol, protocol.responseProfile, responseFieldList(protocol, fields));
}

/**
 * The body-hash field: unpadded base64url SHA-256 over the exact bytes, 43 characters. Over the
 * bytes that are transmitted, never over a second serialisation.
 */
export async function hashBody(body: BufferSource): Promise<string> {
  const digest = await crypto.subtle.digest(DIGEST_ALGORITHM, body);
  return base64UrlEncode(new Uint8Array(digest));
}

/**
 * The MAC over a canonical form, as it appears on the wire.
 *
 * @param key a non-extractable HMAC `CryptoKey` with usage `sign`. Non-extractable blocks export,
 * not use: a script in this origin can still use it as a signing oracle.
 * @param canonical the UTF-8 bytes of a form built above, never a hand-assembled string.
 */
export async function computeMac(key: CryptoKey, canonical: BufferSource): Promise<string> {
  const mac = await crypto.subtle.sign(MAC_ALGORITHM, key, canonical);
  return base64UrlEncode(new Uint8Array(mac));
}

/** Verifies `mac` (raw bytes) over `canonical` with a `verify` key. WebCrypto does the comparison. */
export function verifyMac(key: CryptoKey, mac: CryptoBytes, canonical: BufferSource): Promise<boolean> {
  return crypto.subtle.verify(MAC_ALGORITHM, key, mac, canonical);
}
