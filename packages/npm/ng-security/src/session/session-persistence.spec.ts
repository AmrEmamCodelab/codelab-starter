import { IDBFactory } from 'fake-indexeddb';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { randomKeyBytes, sessionKeysFrom } from '../testing/test-support.ts';
import {
  DEFAULT_SESSION_PERSISTENCE_NAMES,
  indexedDbKeyRecordStore,
  localStorageMarker,
} from './session-persistence.ts';

class MemoryStorage {
  private readonly values = new Map<string, string>();
  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }
  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
  removeItem(key: string): void {
    this.values.delete(key);
  }
}

describe('localStorageMarker', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('sets, reads and clears the marker under its key', () => {
    const storage = new MemoryStorage();
    vi.stubGlobal('localStorage', storage);
    const marker = localStorageMarker('signing');

    expect(marker.isSet()).toBe(false);
    marker.set();
    expect(marker.isSet()).toBe(true);
    expect(storage.getItem('signing')).toBe('1');
    marker.clear();
    expect(marker.isSet()).toBe(false);
  });

  it('reads an unreadable storage as no marker, and warns', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    vi.stubGlobal('localStorage', { getItem: () => { throw new Error('denied'); }, removeItem: () => { throw new Error('denied'); } });
    const marker = localStorageMarker('signing');

    expect(marker.isSet()).toBe(false);
    expect(() => marker.clear()).not.toThrow();
    expect(warn).toHaveBeenCalled();
  });

  it('throws when the marker cannot be stored', () => {
    vi.stubGlobal('localStorage', undefined);
    expect(() => localStorageMarker('signing').set()).toThrow();
  });
});

describe('indexedDbKeyRecordStore', () => {
  beforeEach(() => vi.stubGlobal('indexedDB', new IDBFactory()));
  afterEach(() => vi.unstubAllGlobals());

  it('reports availability from the environment', () => {
    expect(indexedDbKeyRecordStore(DEFAULT_SESSION_PERSISTENCE_NAMES).isAvailable()).toBe(true);
    vi.stubGlobal('indexedDB', undefined);
    expect(indexedDbKeyRecordStore(DEFAULT_SESSION_PERSISTENCE_NAMES).isAvailable()).toBe(false);
  });

  it('round-trips a record holding CryptoKey handles across store instances', async () => {
    const keys = await sessionKeysFrom(randomKeyBytes());
    await indexedDbKeyRecordStore(DEFAULT_SESSION_PERSISTENCE_NAMES).write({ ...keys });

    const read = (await indexedDbKeyRecordStore(DEFAULT_SESSION_PERSISTENCE_NAMES).read()) as typeof keys;

    expect(read.audience).toBe(keys.audience);
    expect(read.sign).toBeInstanceOf(CryptoKey);
  });

  it('reads null before a write and after an erase', async () => {
    const store = indexedDbKeyRecordStore(DEFAULT_SESSION_PERSISTENCE_NAMES);
    expect(await store.read()).toBeNull();

    await store.write({ audience: 'x' });
    await store.erase();
    expect(await store.read()).toBeNull();
  });

  it('turns an unopenable database into a null read and a quiet erase', async () => {
    vi.stubGlobal('indexedDB', { open: () => { throw new Error('blocked by policy'); } });
    const store = indexedDbKeyRecordStore(DEFAULT_SESSION_PERSISTENCE_NAMES);

    expect(await store.read()).toBeNull();
    await expect(store.erase()).resolves.toBeUndefined();
    await expect(store.write({})).rejects.toThrow('blocked by policy');
  });
});
