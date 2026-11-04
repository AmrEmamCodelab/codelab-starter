/**
 * Reference computations the browser package does not ship, written independently of the C#
 * implementation so the golden vectors compare two implementations, not one: the server-side
 * session key derivation (HKDF) and the PublicId codec (AES-256-GCM with a synthetic nonce).
 * Spec support only: excluded from the build. Protocol names and framing come from the package.
 */

import { buildCanonicalBytes, type CryptoBytes } from '../canonical/canonical.ts';
import type { SecurityProtocol } from '../protocol/security-protocol.ts';
import { base64UrlEncode, decodeBase64UrlStrict } from '../signing/base64url.ts';
import { WIRE_ID_PAYLOAD_LENGTH, WIRE_ID_VERSION } from '../wire-id/wire-id.ts';
import { ringKey, type FixtureRing } from './vector-files.ts';

/** A body in a vector file. */
export type VectorBody =
  | { readonly encoding: 'utf8'; readonly text: string }
  | { readonly encoding: 'base64'; readonly base64: string }
  | { readonly encoding: 'repeat'; readonly text: string; readonly count: number };

/** The C# profile constants of the PublicId construction (PublicIdCodec.AadProfile, PublicIdKeyring.KdfProfile). */
export const PUBLIC_ID_AAD_PROFILE = 'PUBLICID-AAD';
export const PUBLIC_ID_KDF_PROFILE = 'PUBLICID-KDF';
export const PublicIdPurposes = Object.freeze({ encryption: 'enc', mac: 'mac' } as const);

const KEY_BITS = 256;
const SHA_256 = 'SHA-256';
const NONCE_BYTES = 12;
const TAG_BYTES = 16;
const BITS_PER_BYTE = 8;
const TAG_BITS = TAG_BYTES * BITS_PER_BYTE;
const ID_BYTES = 8;
const PAYLOAD_BYTES = NONCE_BYTES + TAG_BYTES + ID_BYTES;
const MINIMUM_ID = 1n;
const TOKEN_SEPARATOR = '_';
const TOKEN_SHAPE = new RegExp(`^${WIRE_ID_VERSION}_([a-z0-9]{1,8})_([A-Za-z0-9_-]{${WIRE_ID_PAYLOAD_LENGTH}})$`);
const encoder = new TextEncoder();

/** The exact bytes a vector body describes. */
export function bodyBytes(body: VectorBody): CryptoBytes {
  switch (body.encoding) {
    case 'utf8':
      return encoder.encode(body.text);
    case 'base64':
      return new Uint8Array(Buffer.from(body.base64, 'base64'));
    case 'repeat':
      return encoder.encode(body.text.repeat(body.count));
  }
}

/** HKDF-SHA256 with no salt, 32 bytes out: the derivation both server-side constructions use. */
export async function hkdf(material: CryptoBytes, info: CryptoBytes): Promise<CryptoBytes> {
  const key = await crypto.subtle.importKey('raw', material, 'HKDF', false, ['deriveBits']);
  const bits = await crypto.subtle.deriveBits({ name: 'HKDF', hash: SHA_256, salt: new Uint8Array(0), info }, key, KEY_BITS);
  return new Uint8Array(bits);
}

/** The session-key derivation info: `{P}-SIG-v1/KDF` framing of `[{P}-SIG-v1, sessionId, userId]`. */
export function sessionKeyInfo(protocol: SecurityProtocol, session: { userId: string; sessionId: string }): CryptoBytes {
  return buildCanonicalBytes(protocol, protocol.kdfProfile, [protocol.signatureVersion, session.sessionId, session.userId]);
}

/** HMAC-SHA256 handles over raw key bytes, imported as the session store imports them. */
export async function hmacKeys(bytes: CryptoBytes): Promise<{ sign: CryptoKey; verify: CryptoKey }> {
  const algorithm: HmacImportParams = { name: 'HMAC', hash: SHA_256 };
  return {
    sign: await crypto.subtle.importKey('raw', bytes, algorithm, false, ['sign']),
    verify: await crypto.subtle.importKey('raw', bytes, algorithm, false, ['verify']),
  };
}

/** The AAD of a PublicId token. */
export function publicIdAad(protocol: SecurityProtocol, keyId: string, domain: string): CryptoBytes {
  return buildCanonicalBytes(protocol, PUBLIC_ID_AAD_PROFILE, [protocol.publicIdSpec, keyId, domain]);
}

/** The HKDF info of one PublicId subkey. */
export function publicIdKdfInfo(protocol: SecurityProtocol, keyId: string, purpose: string): CryptoBytes {
  return buildCanonicalBytes(protocol, PUBLIC_ID_KDF_PROFILE, [protocol.publicIdSpec, keyId, purpose]);
}

interface PublicIdSubkeys {
  readonly encryption: CryptoKey;
  readonly mac: CryptoKey;
}

async function subkeys(protocol: SecurityProtocol, ring: FixtureRing, keyId: string): Promise<PublicIdSubkeys> {
  const material = ringKey(ring, keyId);
  const encryption = await hkdf(material, publicIdKdfInfo(protocol, keyId, PublicIdPurposes.encryption));
  const mac = await hkdf(material, publicIdKdfInfo(protocol, keyId, PublicIdPurposes.mac));
  return {
    encryption: await crypto.subtle.importKey('raw', encryption, 'AES-GCM', false, ['encrypt', 'decrypt']),
    mac: await crypto.subtle.importKey('raw', mac, { name: 'HMAC', hash: SHA_256 }, false, ['sign']),
  };
}

async function syntheticNonce(macKey: CryptoKey, aad: CryptoBytes, plaintext: CryptoBytes): Promise<CryptoBytes> {
  const input = new Uint8Array(aad.length + plaintext.length);
  input.set(aad);
  input.set(plaintext, aad.length);
  return new Uint8Array(await crypto.subtle.sign('HMAC', macKey, input)).slice(0, NONCE_BYTES);
}

function idBytes(id: bigint): CryptoBytes {
  const bytes = new Uint8Array(ID_BYTES);
  new DataView(bytes.buffer).setBigInt64(0, id);
  return bytes;
}

/** Mints the token for (keyId, domain, id): nonce | tag | ciphertext, base64url, after `m1_{keyId}_`. */
export async function protectPublicId(
  protocol: SecurityProtocol,
  ring: FixtureRing,
  token: { keyId: string; domain: string; id: bigint },
): Promise<string> {
  const keys = await subkeys(protocol, ring, token.keyId);
  const aad = publicIdAad(protocol, token.keyId, token.domain);
  const plaintext = idBytes(token.id);
  const nonce = await syntheticNonce(keys.mac, aad, plaintext);
  const sealed = new Uint8Array(
    await crypto.subtle.encrypt({ name: 'AES-GCM', iv: nonce, additionalData: aad, tagLength: TAG_BITS }, keys.encryption, plaintext),
  );
  const payload = new Uint8Array(PAYLOAD_BYTES);
  payload.set(nonce);
  payload.set(sealed.subarray(ID_BYTES), NONCE_BYTES);
  payload.set(sealed.subarray(0, ID_BYTES), NONCE_BYTES + TAG_BYTES);
  return [WIRE_ID_VERSION, token.keyId, base64UrlEncode(payload)].join(TOKEN_SEPARATOR);
}

/** The id inside a token under the ring, or null for anything that does not authenticate. */
export async function openPublicId(
  protocol: SecurityProtocol,
  ring: FixtureRing,
  token: { domain: string; value: string },
): Promise<bigint | null> {
  const shape = TOKEN_SHAPE.exec(token.value);
  if (shape === null || !ring.keys.some((key) => key.keyId === shape[1])) return null;
  const payload = decodeBase64UrlStrict(shape[2], PAYLOAD_BYTES);
  if (payload === null) return null;
  const keys = await subkeys(protocol, ring, shape[1]);
  const aad = publicIdAad(protocol, shape[1], token.domain);
  const nonce = payload.slice(0, NONCE_BYTES);
  const sealed = new Uint8Array(TAG_BYTES + ID_BYTES);
  sealed.set(payload.subarray(NONCE_BYTES + TAG_BYTES));
  sealed.set(payload.subarray(NONCE_BYTES, NONCE_BYTES + TAG_BYTES), ID_BYTES);
  let plaintext: CryptoBytes;
  try {
    const algorithm = { name: 'AES-GCM', iv: nonce, additionalData: aad, tagLength: TAG_BITS };
    plaintext = new Uint8Array(await crypto.subtle.decrypt(algorithm, keys.encryption, sealed));
  } catch {
    // A tag mismatch is the expected refusal for a forged or foreign token.
    return null;
  }
  const expectedNonce = await syntheticNonce(keys.mac, aad, plaintext);
  const id = new DataView(plaintext.buffer).getBigInt64(0);
  return bytesEqual(nonce, expectedNonce) && id >= MINIMUM_ID ? id : null;
}

function bytesEqual(left: Uint8Array, right: Uint8Array): boolean {
  return left.length === right.length && left.every((value, index) => value === right[index]);
}
