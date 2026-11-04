/**
 * The browser's session signing key store.
 *
 * The guarantee, at its real size: the raw key bytes from the login response are imported into two
 * NON-EXTRACTABLE `CryptoKey` handles (one `sign`, one `verify`) and the byte array is zeroed.
 * Non-extractability blocks export, not use: a script in this origin can still sign with the
 * handles. This removes the "dump storage, keep a portable offline key" outcome and nothing more;
 * it is not XSS resistance and must never be described as such.
 *
 * Failure is closed and loud: no WebCrypto (any plain-HTTP non-localhost origin), no key store or a
 * refused marker makes {@link SessionKeyStore.adopt} throw, so login refuses rather than starting a
 * session that cannot sign.
 */

import { assertSigningBlock, decodeSessionKey, SigningEnvironmentUnsupportedError } from './signing-material.ts';
import type { SigningSessionBlock } from './signing-material.ts';
import type { KeyRecordStore, SigningSessionMarker } from './session-persistence.ts';

/** What the store hands back: two opaque handles and the fields that ride with them. */
export interface SessionSigningKeys {
  /** Non-extractable, usage `sign`. Used by the request signer. */
  readonly sign: CryptoKey;
  /** Non-extractable, usage `verify` only. The same key imported twice. */
  readonly verify: CryptoKey;
  /** The audience every request proof carries. */
  readonly audience: string;
  /** Whether the response verifier runs. */
  readonly verifyResponses: boolean;
}

/** Milliseconds since the Unix epoch. Injected so time is testable (the TypeScript TimeProvider). */
export type EpochClock = () => number;

/** What the store needs from its environment. */
export interface SessionKeyStoreDependencies {
  readonly marker: SigningSessionMarker;
  readonly records: KeyRecordStore;
  readonly clock: EpochClock;
}

const MILLISECONDS_PER_SECOND = 1000;
const KEY_ALGORITHM: HmacImportParams = Object.freeze({ name: 'HMAC', hash: 'SHA-256' });
const RAW_FORMAT = 'raw';
const NOT_EXTRACTABLE = false;

/**
 * Holds the session's signing keys for the lifetime of the access token. Provide ONE per
 * application (see `provideCodelabSecurity`): the in-memory cache is per tab, the record is shared
 * across tabs of the origin, so a second tab works with no handoff.
 */
export class SessionKeyStore {
  private cached: SessionSigningKeys | null = null;

  /**
   * Server clock minus local clock, in seconds. In memory only: a stored offset outlives the
   * measurement, and a laptop resuming from sleep would apply a stale one. After a reload it is 0 and
   * the server's freshness window absorbs the drift; a stale proof gets one re-sign.
   */
  private clockOffsetSeconds = 0;

  constructor(private readonly dependencies: SessionKeyStoreDependencies) {}

  /** True when this session was issued signing material. The ONLY per-request work when signing is off. */
  expectsSigning(): boolean {
    return this.dependencies.marker.isSet();
  }

  /** Whether this browser can hold and use a session key at all. */
  isSupported(): boolean {
    return typeof crypto !== 'undefined' && typeof crypto.subtle !== 'undefined' && this.dependencies.records.isAvailable();
  }

  /** Whole Unix seconds for a proof, corrected by the offset measured at login. */
  nowUnixSeconds(): number {
    return Math.floor(this.dependencies.clock() / MILLISECONDS_PER_SECOND) + this.clockOffsetSeconds;
  }

  /** The offset measured at login, in seconds; for a clock-skew metric. */
  clockSkewSeconds(): number {
    return this.clockOffsetSeconds;
  }

  /**
   * Takes the login response's `signing` block, imports the key twice and zeroes the bytes. Replaces
   * any previous session's keys, so a second login never inherits the first session's key.
   *
   * @throws SigningEnvironmentUnsupportedError when this browser cannot hold or use the key.
   * @throws SigningMaterialRejectedError when the block is malformed.
   */
  async adopt(block: SigningSessionBlock): Promise<SessionSigningKeys> {
    // A refusal below must leave NO session behind, never the previous one.
    await this.clear();
    if (!this.isSupported()) {
      throw new SigningEnvironmentUnsupportedError(
        'This browser cannot hold a session signing key: WebCrypto or IndexedDB is unavailable. ' +
          'WebCrypto requires a secure context, so a plain-HTTP host other than localhost always fails here.',
      );
    }
    assertSigningBlock(block);
    const bytes = decodeSessionKey(block.sessionSigningKey);
    try {
      const keys: SessionSigningKeys = {
        sign: await crypto.subtle.importKey(RAW_FORMAT, bytes, KEY_ALGORITHM, NOT_EXTRACTABLE, ['sign']),
        verify: await crypto.subtle.importKey(RAW_FORMAT, bytes, KEY_ALGORITHM, NOT_EXTRACTABLE, ['verify']),
        audience: block.audience,
        verifyResponses: block.verifyResponses === true,
      };
      await this.persist(keys);
      this.cached = keys;
      this.clockOffsetSeconds = block.serverTimeUnix - Math.floor(this.dependencies.clock() / MILLISECONDS_PER_SECOND);
      return keys;
    } finally {
      // This module keeps no key bytes after import. The base64 string in the login response is
      // immutable and cannot be erased; it becomes garbage when the caller drops the response.
      bytes.fill(0);
    }
  }

  /**
   * The session's keys, or null. Null means either no signing session (pass through unsigned) or a
   * signing session whose key is gone (fail closed); {@link expectsSigning} tells them apart.
   */
  async load(): Promise<SessionSigningKeys | null> {
    if (!this.expectsSigning()) {
      // The marker is the authority: without it any cached handle belongs to an ended session.
      this.cached = null;
      return null;
    }
    if (this.cached !== null) return this.cached;
    if (!this.isSupported()) return null;

    const record = await this.dependencies.records.read();
    if (!isStoredKeyRecord(record)) return null;
    this.cached = {
      sign: record.sign,
      verify: record.verify,
      audience: record.audience,
      verifyResponses: record.verifyResponses === true,
    };
    return this.cached;
  }

  /** Drops the keys, the marker and the measured offset. Call it from the session clear on logout. */
  async clear(): Promise<void> {
    this.cached = null;
    this.clockOffsetSeconds = 0;
    this.dependencies.marker.clear();
    if (this.dependencies.records.isAvailable()) await this.dependencies.records.erase();
  }

  /** Writes the record, then the marker; a refused marker leaves no record behind. */
  private async persist(keys: SessionSigningKeys): Promise<void> {
    await this.dependencies.records.write({ ...keys });
    try {
      this.dependencies.marker.set();
    } catch (error) {
      await this.dependencies.records.erase();
      // Without the marker a reload would read "no signing session" and send unsigned requests.
      throw new SigningEnvironmentUnsupportedError(
        `The signing-session marker could not be stored (${String(error)}), so after a reload this ` +
          'browser could not tell a signing session from an unsigned one.',
      );
    }
  }
}

interface StoredKeyRecord {
  readonly sign: CryptoKey;
  readonly verify: CryptoKey;
  readonly audience: string;
  readonly verifyResponses?: unknown;
}

/**
 * A stored value is a key record only if it carries two real `CryptoKey` handles. IndexedDB is
 * writable by anything in the origin, so the record is input: this stops a stale or corrupted row
 * from failing as an unexplained `TypeError` deep inside an interceptor. It is not a security control.
 */
export function isStoredKeyRecord(value: unknown): value is StoredKeyRecord {
  if (typeof value !== 'object' || value === null || typeof CryptoKey === 'undefined') return false;
  const candidate = value as Partial<StoredKeyRecord>;
  return (
    candidate.sign instanceof CryptoKey &&
    candidate.verify instanceof CryptoKey &&
    typeof candidate.audience === 'string'
  );
}
