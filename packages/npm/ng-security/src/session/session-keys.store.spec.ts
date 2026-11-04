import { beforeEach, describe, expect, it } from 'vitest';
import { MemoryMarker, MemoryRecordStore, randomKeyBytes, signingBlock } from '../testing/test-support.ts';
import { computeMac } from '../signing/signature-canonical.ts';
import { SessionKeyStore, isStoredKeyRecord } from './session-keys.store.ts';
import { SigningEnvironmentUnsupportedError, SigningMaterialRejectedError } from './signing-material.ts';

const SAMPLE = new TextEncoder().encode('sample');
const SERVER_TIME = 1_900_000_000;
const MS = 1000;

let marker: MemoryMarker;
let records: MemoryRecordStore;
let nowMs: number;

function newStore(): SessionKeyStore {
  return new SessionKeyStore({ marker, records, clock: () => nowMs });
}

beforeEach(() => {
  marker = new MemoryMarker();
  records = new MemoryRecordStore();
  nowMs = SERVER_TIME * MS;
});

describe('SessionKeyStore', () => {
  it('holds nothing before a session is adopted', async () => {
    const store = newStore();
    expect(store.expectsSigning()).toBe(false);
    expect(await store.load()).toBeNull();
  });

  it('imports the key non-extractably, so the bytes cannot be read back out', async () => {
    const keys = await newStore().adopt(signingBlock(randomKeyBytes()));

    expect(keys.sign.extractable).toBe(false);
    await expect(crypto.subtle.exportKey('raw', keys.sign)).rejects.toThrow();
  });

  it('splits the usages: one handle signs, the other only verifies', async () => {
    const keys = await newStore().adopt(signingBlock(randomKeyBytes()));

    expect(keys.sign.usages).toEqual(['sign']);
    expect(keys.verify.usages).toEqual(['verify']);
    await expect(computeMac(keys.verify, SAMPLE)).rejects.toThrow();
  });

  it('survives a reload through the record store, in another store instance', async () => {
    const adopted = await newStore().adopt(signingBlock(randomKeyBytes(), { verifyResponses: false }));
    const reloaded = await newStore().load();

    expect(reloaded).not.toBeNull();
    expect(await computeMac(reloaded!.sign, SAMPLE)).toBe(await computeMac(adopted.sign, SAMPLE));
    expect(reloaded!.audience).toBe(adopted.audience);
    expect(reloaded!.verifyResponses).toBe(false);
  });

  it('serves repeated loads from the per-tab cache', async () => {
    const store = newStore();
    await store.adopt(signingBlock(randomKeyBytes()));
    records.record = null;

    expect(await store.load()).not.toBeNull();
  });

  it('clear() removes the key, the marker and the record', async () => {
    const store = newStore();
    await store.adopt(signingBlock(randomKeyBytes()));
    await store.clear();

    expect(marker.value).toBe(false);
    expect(records.record).toBeNull();
    expect(await store.load()).toBeNull();
    expect(await newStore().load()).toBeNull();
  });

  it('does not let a second session inherit the first session key', async () => {
    const store = newStore();
    const first = await store.adopt(signingBlock(randomKeyBytes()));
    const second = await store.adopt(signingBlock(randomKeyBytes()));

    expect(await computeMac(second.sign, SAMPLE)).not.toBe(await computeMac(first.sign, SAMPLE));
    expect(await computeMac((await newStore().load())!.sign, SAMPLE)).not.toBe(await computeMac(first.sign, SAMPLE));
  });

  it('refuses to hand back a record whose session marker is gone', async () => {
    const store = newStore();
    await store.adopt(signingBlock(randomKeyBytes()));
    marker.clear();

    expect(store.expectsSigning()).toBe(false);
    expect(await store.load()).toBeNull();
  });

  it('returns null for a signing session whose record is gone or corrupted', async () => {
    await newStore().adopt(signingBlock(randomKeyBytes()));
    records.record = { sign: 'not a key', verify: null, audience: 1 };

    expect(await newStore().load()).toBeNull();
    expect(marker.value).toBe(true);
  });

  describe('the clock offset', () => {
    it('is measured once from the login response', async () => {
      nowMs = (SERVER_TIME - 500) * MS;
      const store = newStore();
      await store.adopt(signingBlock(randomKeyBytes(), { serverTimeUnix: SERVER_TIME }));

      expect(store.clockSkewSeconds()).toBe(500);
      expect(store.nowUnixSeconds()).toBe(SERVER_TIME);
      nowMs += 10 * MS;
      expect(store.nowUnixSeconds()).toBe(SERVER_TIME + 10);
    });

    it('is not persisted across a reload', async () => {
      nowMs = (SERVER_TIME - 500) * MS;
      await newStore().adopt(signingBlock(randomKeyBytes(), { serverTimeUnix: SERVER_TIME }));

      expect(newStore().clockSkewSeconds()).toBe(0);
    });

    it('always yields whole seconds', () => {
      nowMs = SERVER_TIME * MS + 999;
      expect(newStore().nowUnixSeconds()).toBe(SERVER_TIME);
    });
  });

  describe('refusing malformed signing material', () => {
    it.each([
      ['an algorithm this build does not implement', { alg: 'none' }],
      ['material that is not base64', { sessionSigningKey: '***' }],
      ['an empty key', { sessionSigningKey: '' }],
      ['an empty audience', { audience: '' }],
      ['a server time it cannot measure skew from', { serverTimeUnix: 0 }],
      ['a fractional server time', { serverTimeUnix: 1.5 }],
    ])('refuses %s', async (_label, overrides) => {
      await expect(newStore().adopt(signingBlock(randomKeyBytes(), overrides))).rejects.toBeInstanceOf(
        SigningMaterialRejectedError,
      );
    });

    it('refuses a key shorter than 32 decoded bytes', async () => {
      await expect(newStore().adopt(signingBlock(randomKeyBytes(31)))).rejects.toBeInstanceOf(SigningMaterialRejectedError);
    });

    it('leaves no session behind after a refusal, not even the previous one', async () => {
      const store = newStore();
      await store.adopt(signingBlock(randomKeyBytes()));
      await expect(store.adopt(signingBlock(randomKeyBytes(), { alg: 'none' }))).rejects.toThrow();

      expect(store.expectsSigning()).toBe(false);
      expect(await store.load()).toBeNull();
      expect(records.record).toBeNull();
    });
  });

  describe('an environment that cannot sign', () => {
    it('refuses to adopt without a key record store', async () => {
      records.available = false;
      const store = newStore();

      expect(store.isSupported()).toBe(false);
      await expect(store.adopt(signingBlock(randomKeyBytes()))).rejects.toBeInstanceOf(SigningEnvironmentUnsupportedError);
    });

    it('refuses when the marker cannot be stored, and erases the record it wrote', async () => {
      marker.failOnSet = true;
      const store = newStore();

      await expect(store.adopt(signingBlock(randomKeyBytes()))).rejects.toBeInstanceOf(SigningEnvironmentUnsupportedError);
      expect(records.writes).toBe(1);
      expect(records.record).toBeNull();
    });

    it('reports this environment as able to hold a session key', () => {
      expect(newStore().isSupported()).toBe(true);
    });
  });
});

describe('isStoredKeyRecord', () => {
  it('accepts only two CryptoKey handles and a string audience', async () => {
    const keys = await newStore().adopt(signingBlock(randomKeyBytes()));

    expect(isStoredKeyRecord({ ...keys })).toBe(true);
    expect(isStoredKeyRecord({ ...keys, audience: 1 })).toBe(false);
    expect(isStoredKeyRecord({ ...keys, sign: {} })).toBe(false);
    expect(isStoredKeyRecord(null)).toBe(false);
    expect(isStoredKeyRecord('record')).toBe(false);
  });
});
