/**
 * Spec helpers. Excluded from the build. Key material is generated at RUNTIME from a CSPRNG: no
 * key-shaped literal is ever committed, and no fixture key of any other system is used.
 */

import { createSecurityProtocol, TEST_PROTOCOL_PREFIX, type SecurityProtocol } from '../protocol/security-protocol.ts';
import type { SessionSigningKeys } from '../session/session-keys.store.ts';
import type { KeyRecordStore, SigningSessionMarker } from '../session/session-persistence.ts';
import type { SigningSessionBlock } from '../session/signing-material.ts';

export const TEST_PROTOCOL: SecurityProtocol = createSecurityProtocol(TEST_PROTOCOL_PREFIX);
export const TEST_AUDIENCE = 'Starter.Api';
export const KEY_BYTES = 32;
const HMAC: HmacImportParams = { name: 'HMAC', hash: 'SHA-256' };

/** Fresh random key bytes. */
export function randomKeyBytes(length = KEY_BYTES): Uint8Array<ArrayBuffer> {
  return crypto.getRandomValues(new Uint8Array(length));
}

/** Standard base64 of raw bytes, as the login response carries it. */
export function base64Of(bytes: Uint8Array): string {
  return Buffer.from(bytes).toString('base64');
}

/** Session keys imported exactly as the store imports them: non-extractable, one handle per usage. */
export async function sessionKeysFrom(
  bytes: Uint8Array<ArrayBuffer>,
  overrides: Partial<SessionSigningKeys> = {},
): Promise<SessionSigningKeys> {
  return {
    sign: await crypto.subtle.importKey('raw', bytes, HMAC, false, ['sign']),
    verify: await crypto.subtle.importKey('raw', bytes, HMAC, false, ['verify']),
    audience: TEST_AUDIENCE,
    verifyResponses: true,
    ...overrides,
  };
}

/** A login `signing` block over `bytes`. */
export function signingBlock(
  bytes: Uint8Array,
  overrides: Partial<SigningSessionBlock> = {},
): SigningSessionBlock {
  return {
    alg: 'HMAC-SHA256',
    sessionSigningKey: base64Of(bytes),
    audience: TEST_AUDIENCE,
    verifyResponses: true,
    serverTimeUnix: 1_900_000_000,
    ...overrides,
  };
}

/** An in-memory marker; `failOnSet` simulates storage refusing the write. */
export class MemoryMarker implements SigningSessionMarker {
  value = false;
  failOnSet = false;

  isSet(): boolean {
    return this.value;
  }

  set(): void {
    if (this.failOnSet) throw new Error('quota exceeded');
    this.value = true;
  }

  clear(): void {
    this.value = false;
  }
}

/** An in-memory key record store shared between "tabs" by sharing the instance. */
export class MemoryRecordStore implements KeyRecordStore {
  record: unknown = null;
  available = true;
  writes = 0;

  isAvailable(): boolean {
    return this.available;
  }

  async write(record: unknown): Promise<void> {
    this.writes += 1;
    this.record = record;
  }

  async read(): Promise<unknown> {
    return this.record;
  }

  async erase(): Promise<void> {
    this.record = null;
  }
}

/** Bytes of a string as a standalone ArrayBuffer (what an `arraybuffer` response body is). */
export function bufferOf(text: string): ArrayBuffer {
  const bytes = new TextEncoder().encode(text);
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
}
