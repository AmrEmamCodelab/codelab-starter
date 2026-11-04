/**
 * The login response's `signing` block and its validation. Absent entirely when the server's
 * request signing mode is `Off`.
 */

import type { CryptoBytes } from '../canonical/canonical.ts';

/** The only algorithm this build implements. An unknown one is refused, never guessed at. */
export const SUPPORTED_SIGNING_ALGORITHM = 'HMAC-SHA256';

/** The server's key-ring validator enforces the same floor. */
export const MIN_SESSION_KEY_BYTES = 32;

/** Reasons a store refusal carries, so a login screen can branch without matching messages. */
export const SigningRefusalReasons = Object.freeze({
  unsupported: 'signing_unsupported',
  materialRejected: 'signing_material_rejected',
  keyMissing: 'signing_key_missing',
} as const);

/** The `signing` block of a login response. */
export interface SigningSessionBlock {
  /** Always {@link SUPPORTED_SIGNING_ALGORITHM}; present so an algorithm change is a visible wire change. */
  readonly alg: string;
  /** Standard base64 of the raw session key bytes. DECODED before import, never used as text. */
  readonly sessionSigningKey: string;
  /** The deployment audience. A signed field, so it must match the server byte for byte. */
  readonly audience: string;
  /** Mirrors the server's response signing mode. One server value drives both ends. */
  readonly verifyResponses: boolean;
  /** The server's Unix seconds at issue; the clock offset is measured from it once. */
  readonly serverTimeUnix: number;
}

/** This browser cannot sign (no WebCrypto, no key store, no session marker). Raised at login. */
export class SigningEnvironmentUnsupportedError extends Error {
  readonly reason = SigningRefusalReasons.unsupported;

  constructor(message: string) {
    super(message);
    this.name = 'SigningEnvironmentUnsupportedError';
  }
}

/** The login response's `signing` block is malformed: a deployment fault, surfaced at login. */
export class SigningMaterialRejectedError extends Error {
  readonly reason = SigningRefusalReasons.materialRejected;

  constructor(message: string) {
    super(message);
    this.name = 'SigningMaterialRejectedError';
  }
}

/**
 * Checks every field except the key bytes.
 *
 * @throws SigningMaterialRejectedError naming the first problem found.
 */
export function assertSigningBlock(block: SigningSessionBlock): void {
  if (block.alg !== SUPPORTED_SIGNING_ALGORITHM) {
    throw new SigningMaterialRejectedError(
      `Unsupported signing algorithm. This build implements ${SUPPORTED_SIGNING_ALGORITHM} only.`,
    );
  }
  if (typeof block.audience !== 'string' || block.audience.length === 0) {
    throw new SigningMaterialRejectedError(
      'The signing block carries no audience. The audience is a signed field, so an empty one ' +
        'produces a canonical form the server cannot reconstruct.',
    );
  }
  if (!Number.isInteger(block.serverTimeUnix) || block.serverTimeUnix <= 0) {
    throw new SigningMaterialRejectedError(
      'The signing block carries no usable server time, so clock skew cannot be measured.',
    );
  }
}

/**
 * Standard base64 to raw key bytes, at least {@link MIN_SESSION_KEY_BYTES} long. The caller zeroes
 * the returned array after import.
 *
 * @throws SigningMaterialRejectedError on absent, non-base64 or short material.
 */
export function decodeSessionKey(value: string): CryptoBytes {
  if (typeof value !== 'string' || value.length === 0) {
    throw new SigningMaterialRejectedError('The signing block carries no key material.');
  }
  const bytes = decodeBase64(value);
  if (bytes.length < MIN_SESSION_KEY_BYTES) {
    bytes.fill(0);
    throw new SigningMaterialRejectedError(
      `A session signing key must decode to at least ${MIN_SESSION_KEY_BYTES} bytes.`,
    );
  }
  return bytes;
}

function decodeBase64(value: string): CryptoBytes {
  let binary: string;
  try {
    binary = atob(value);
  } catch (error) {
    throw new SigningMaterialRejectedError(`The session signing key is not valid base64 (${String(error)}).`);
  }
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
}
