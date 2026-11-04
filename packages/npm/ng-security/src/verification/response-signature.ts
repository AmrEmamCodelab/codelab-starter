/**
 * `{Prefix}-SIG-v1` response verification over the bytes that arrived.
 *
 * A verified response proves that the bytes, status and `Content-Type` were produced under this
 * session's key and bound to the nonce this browser sent: integrity and anti-replay, nothing more.
 * The digest is over the exact received bytes; nothing here parses, re-serialises or coerces a body
 * before hashing it.
 */

import type { CryptoBytes } from '../canonical/canonical.ts';
import type { SecurityProtocol } from '../protocol/security-protocol.ts';
import { decodeBase64UrlStrict } from '../signing/base64url.ts';
import { UNSIGNED_BODY, buildResponseSigningInputBytes, hashBody, verifyMac } from '../signing/signature-canonical.ts';
import { MAC_BYTES, parseResponseSignatureHeader } from '../signing/signature-header.ts';

/**
 * Why a response was refused: three distinguishable causes. Spelled differently from the server's
 * `signature_*` problem codes, so a log line cannot be read as the opposite of what happened.
 */
export const ResponseSignatureFailures = Object.freeze({
  missing: 'response_signature_missing',
  malformed: 'response_signature_malformed',
  mismatch: 'response_signature_mismatch',
} as const);

export type ResponseSignatureFailure =
  (typeof ResponseSignatureFailures)[keyof typeof ResponseSignatureFailures];

const ERROR_NAME = 'ResponseSignatureError';

/**
 * A response failed verification. Deliberately NOT an `HttpErrorResponse`: an auth interceptor ends
 * the session on a 401 and a forbidden interceptor resyncs permissions on a 403, so a client-side
 * mismatch wearing an HTTP status would become a random logout. Protection by TYPE, which no later
 * edit to a string list can undo. Callers treat it as fatal for the call: an unverified response is
 * never rendered as data.
 */
export class ResponseSignatureError extends Error {
  readonly reason: ResponseSignatureFailure;
  /** The status the response carried, for diagnosis only: an unverified status is not trusted. */
  readonly status: number;
  readonly url: string | null;

  constructor(reason: ResponseSignatureFailure, context: { status: number; url: string | null }) {
    super(
      `${reason}: the integrity of the ${context.status} response from ${context.url ?? 'an unknown URL'} ` +
        'could not be confirmed, so its body was not delivered.',
    );
    // instanceof is load-bearing (see above); a downlevelled class build would break it silently.
    Object.setPrototypeOf(this, ResponseSignatureError.prototype);
    this.name = ERROR_NAME;
    this.reason = reason;
    this.status = context.status;
    this.url = context.url;
  }
}

/** True for a {@link ResponseSignatureError}, including one from another bundle copy of this class. */
export function isResponseSignatureError(value: unknown): value is ResponseSignatureError {
  if (value instanceof ResponseSignatureError) return true;
  return typeof value === 'object' && value !== null && (value as { name?: unknown }).name === ERROR_NAME;
}

/** A response exactly as received. */
export interface ReceivedResponse {
  readonly status: number;
  /** The exact `Content-Type` value, or `''`. */
  readonly contentType: string;
  /** The exact bytes received, or null for no body. Never a parsed object. */
  readonly body: ArrayBuffer | null;
  /** The raw response signature header value, or null. */
  readonly signatureHeader: string | null;
  /** For the error message only. */
  readonly url: string | null;
}

/** The key and the request a response is checked against. */
export interface VerificationContext {
  readonly protocol: SecurityProtocol;
  /** The `verify`-only session key handle. */
  readonly key: CryptoKey;
  /** The nonce THIS browser sent on the request being answered. */
  readonly requestNonce: string;
}

const STATUS_OK = 200;
const STATUS_NO_CONTENT = 204;
const STATUS_NOT_MODIFIED = 304;
const STATUS_CLIENT_ERROR = 400;

/**
 * May this response arrive with no signature header? A closed list, never a URL list: below 200
 * (0 is a transport failure), 204 and 304 (no body is buffered, nothing is signed), and every error
 * status (rate limiter, CORS preflight, global exception handler and the verifier's own 401s are
 * produced upstream of the signer and have no nonce to bind to). An exemption excuses a MISSING
 * header, never a wrong one; a 2xx with no header is refused.
 */
export function mayOmitResponseSignature(status: number): boolean {
  if (status < STATUS_OK) return true;
  if (status === STATUS_NO_CONTENT || status === STATUS_NOT_MODIFIED) return true;
  return status >= STATUS_CLIENT_ERROR;
}

const NO_BODY = new ArrayBuffer(0);

/**
 * Verifies a received response, or throws {@link ResponseSignatureError}. Resolves only when the MAC
 * verifies or the response may omit the header; there is no "warn and continue".
 */
export async function verifyResponseSignature(
  context: VerificationContext,
  response: ReceivedResponse,
): Promise<void> {
  if (response.signatureHeader === null || response.signatureHeader === undefined) {
    if (mayOmitResponseSignature(response.status)) return;
    throw new ResponseSignatureError(ResponseSignatureFailures.missing, response);
  }
  const mac = parseResponseSignatureHeader(response.signatureHeader);
  const macBytes = mac === null ? null : decodeBase64UrlStrict(mac, MAC_BYTES);
  if (macBytes === null) throw new ResponseSignatureError(ResponseSignatureFailures.malformed, response);

  // The strong form first: it covers the body.
  const bodyHash = await hashBody(response.body ?? NO_BODY);
  if (await macVerifies({ context, response, macBytes, bodyHash })) return;
  // Then the exempt form (a whole-file download signed with the marker). Safe to try because the
  // marker is INSIDE the MAC: it cannot be forged onto a normal response. The client keeps no list
  // of which endpoints those are.
  if (await macVerifies({ context, response, macBytes, bodyHash: UNSIGNED_BODY })) return;
  throw new ResponseSignatureError(ResponseSignatureFailures.mismatch, response);
}

interface MacCheck {
  readonly context: VerificationContext;
  readonly response: ReceivedResponse;
  readonly macBytes: CryptoBytes;
  readonly bodyHash: string;
}

/**
 * One MAC check against one candidate body-hash field. A key without `verify` usage rejects with a
 * DOMException, deliberately NOT mapped to a mismatch: that would send the next reader hunting a
 * canonicalisation bug that does not exist.
 */
function macVerifies(check: MacCheck): Promise<boolean> {
  const canonical = buildResponseSigningInputBytes(check.context.protocol, {
    requestNonce: check.context.requestNonce,
    status: check.response.status,
    contentType: check.response.contentType,
    bodyHash: check.bodyHash,
  });
  return verifyMac(check.context.key, check.macBytes, canonical);
}
