/**
 * Turns an outgoing `HttpRequest` into a signed one: the `{Prefix}-SIG-v1` request proof.
 *
 * This file decides WHAT enters the canonical form; `signature-canonical.ts` builds it. A second
 * canonicaliser is the most expensive mistake available here: the two would agree for months and
 * then disagree on one Arabic path.
 */

import { HttpContextToken, type HttpRequest } from '@angular/common/http';
import { queryOf } from '../canonical/canonical.ts';
import type { SecurityProtocol } from '../protocol/security-protocol.ts';
import type { SessionSigningKeys } from '../session/session-keys.store.ts';
import { base64UrlEncode } from './base64url.ts';
import { UNSIGNED_BODY, buildRequestSigningInputBytes, computeMac, hashBody } from './signature-canonical.ts';
import { NONCE_BYTES, formatSignatureHeader, timestampDigits } from './signature-header.ts';

/**
 * The nonce this request was signed with, on the request's `HttpContext`: per request, survives
 * `clone()`, never reaches the wire. The response verifier learns the nonce from here, because the
 * server deliberately does not echo it. Null means the request was not signed.
 */
export const SIGNED_REQUEST_NONCE = new HttpContextToken<string | null>(() => null);

const CONTENT_TYPE_HEADER = 'Content-Type';

/** A signed request and the fields a response verifier needs. */
export interface SignedRequest {
  /** The request to send. Its body is the exact serialisation that was hashed. */
  readonly request: HttpRequest<unknown>;
  readonly nonce: string;
  readonly timestamp: string;
}

/** Everything a proof needs besides the request. */
export interface SigningContext {
  readonly protocol: SecurityProtocol;
  /** The service-relative path from the URL classifier. No prefix arithmetic happens here. */
  readonly servicePath: string;
  /** Only the `sign` handle and the audience are used. */
  readonly keys: SessionSigningKeys;
  /** The skew-corrected clock, from `SessionKeyStore.nowUnixSeconds()`. */
  readonly nowUnixSeconds: number;
  /**
   * A fixed nonce instead of a fresh CSPRNG draw. A seam for the golden vectors ONLY: reusing a
   * nonce in production is a replay of your own request, which the server refuses.
   */
  readonly nonce?: string;
}

/**
 * The signing layer refused to produce a proof, so nothing was sent. Deliberately NOT an
 * `HttpErrorResponse`: an auth interceptor reads a 401 as session expiry, and a local signing fault
 * dressed as one becomes a random logout.
 */
export class RequestNotSignedError extends Error {
  readonly reason = 'request_not_signed';

  constructor(message: string) {
    super(message);
    this.name = 'RequestNotSignedError';
  }
}

/** 16 fresh CSPRNG bytes as 22 base64url characters. Fresh PER REQUEST: that is the whole point. */
export function randomNonce(): string {
  const bytes = new Uint8Array(NONCE_BYTES);
  crypto.getRandomValues(bytes);
  return base64UrlEncode(bytes);
}

/** The body hash, and the exact serialisation to transmit when serialisation produced a new value. */
interface BodyPlan {
  readonly bodyHash: string;
  readonly transmit?: string;
}

/**
 * Serialises the body ONCE through Angular's own `serializeBody()`, hashes exactly that, and hands it
 * back to be sent: there is no second serialisation to disagree with.
 */
async function planBody(request: HttpRequest<unknown>): Promise<BodyPlan> {
  const serialized = request.serializeBody();
  // A NULL test, never truthiness: `0`, `false` and `''` are bodies and must be signed.
  if (serialized === null) return { bodyHash: await hashBody(new Uint8Array(0)) };
  // The browser builds the multipart envelope (boundary included) after this runs, so those bytes do
  // not exist yet. The marker is admitted only where the endpoint is exempted on the server.
  if (serialized instanceof FormData) return { bodyHash: UNSIGNED_BODY };
  if (serialized instanceof Blob) return { bodyHash: await hashBody(await serialized.arrayBuffer()) };
  if (serialized instanceof ArrayBuffer) return { bodyHash: await hashBody(serialized) };
  if (serialized instanceof URLSearchParams) {
    return { bodyHash: await hashBody(new TextEncoder().encode(serialized.toString())) };
  }
  return { bodyHash: await hashBody(new TextEncoder().encode(serialized)), transmit: serialized };
}

/**
 * Signs a request and returns the clone to send. Fails closed: any refusal throws
 * {@link RequestNotSignedError}; there is no branch that returns the unsigned original.
 */
export async function signRequest(
  request: HttpRequest<unknown>,
  context: SigningContext,
): Promise<SignedRequest> {
  const timestamp = timestampDigits(context.nowUnixSeconds);
  if (timestamp === null) {
    throw new RequestNotSignedError('The signing clock did not yield whole Unix seconds in range.');
  }
  const nonce = context.nonce ?? randomNonce();
  const plan = await planBody(request);
  // The query is cut from urlWithParams, the URL as it will be sent, never from `request.url`.
  const canonical = buildRequestSigningInputBytes(context.protocol, {
    audience: context.keys.audience,
    method: request.method,
    path: context.servicePath,
    query: queryOf(request.urlWithParams),
    bodyHash: plan.bodyHash,
    timestamp,
    nonce,
  });
  const header = formatSignatureHeader({ timestamp, nonce, mac: await computeMac(context.keys.sign, canonical) });
  if (header === null) {
    throw new RequestNotSignedError(
      `The computed proof does not satisfy the ${context.protocol.signatureHeader} grammar, so nothing was sent.`,
    );
  }
  const signed = cloneSigned(request, { header: { [context.protocol.signatureHeader]: header }, plan });
  // clone() shares the HttpContext instance, so this is visible to this request's chain only.
  signed.context.set(SIGNED_REQUEST_NONCE, nonce);
  return { request: signed, nonce, timestamp };
}

interface SignedClone {
  readonly header: Record<string, string>;
  readonly plan: BodyPlan;
}

function cloneSigned(request: HttpRequest<unknown>, clone: SignedClone): HttpRequest<unknown> {
  const setHeaders = { ...clone.header };
  // Once the body is its own JSON text, Angular would infer text/plain; pin the type the ORIGINAL
  // body implied, unless the caller set one.
  const contentType = request.detectContentTypeHeader();
  if (clone.plan.transmit !== undefined && contentType !== null && !request.headers.has(CONTENT_TYPE_HEADER)) {
    setHeaders[CONTENT_TYPE_HEADER] = contentType;
  }
  return clone.plan.transmit !== undefined
    ? request.clone({ body: clone.plan.transmit, setHeaders })
    : request.clone({ setHeaders });
}
