import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { BROWSER_STORAGE } from '../session/app-storage';
import { MemoryStorage } from '../session/memory-storage';
import { StorageKeys } from '../session/storage-keys';
import { SignInMode } from './sign-in-mode';
import { TokenStore } from './token.store';

const RESPONSE = {
  accessToken: 'a1',
  refreshToken: 'r1',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  idleTimeoutMinutes: 20,
  user: { id: 'u1', displayName: 'Dev User' },
};

function setUp(storage: Storage): TokenStore {
  TestBed.configureTestingModule({ providers: [{ provide: BROWSER_STORAGE, useValue: storage }] });
  return TestBed.inject(TokenStore);
}

describe('TokenStore', () => {
  let storage: MemoryStorage;

  beforeEach(() => {
    storage = new MemoryStorage();
  });

  it('starts signed out with empty storage', () => {
    const store = setUp(storage);
    expect(store.session()).toBeNull();
    expect(store.accessToken()).toBeNull();
    expect(store.refreshToken()).toBeNull();
    expect(store.user()).toBeNull();
  });

  it('saves a response in memory and in storage', () => {
    const store = setUp(storage);
    const session = store.save(RESPONSE, SignInMode.Basic);
    expect(session.accessTokenExpiresAt).toBe(Date.parse(RESPONSE.accessTokenExpiresAt));
    expect(store.accessToken()).toBe('a1');
    expect(store.refreshToken()).toBe('r1');
    expect(store.user()).toEqual(RESPONSE.user);
    expect(storage.getItem(StorageKeys.AccessToken)).toBe('a1');
    expect(storage.getItem(StorageKeys.IdleTimeoutMinutes)).toBe('20');
  });

  it('restores a stored session on start', () => {
    setUp(storage).save(RESPONSE, SignInMode.Basic);
    TestBed.resetTestingModule();
    const restored = setUp(storage);
    expect(restored.session()?.idleTimeoutMinutes).toBe(20);
    expect(restored.user()).toEqual(RESPONSE.user);
  });

  it('treats a partial or corrupt stored session as no session', () => {
    setUp(storage).save(RESPONSE, SignInMode.Basic);
    storage.setItem(StorageKeys.SessionUser, '{not json');
    TestBed.resetTestingModule();
    expect(setUp(storage).session()).toBeNull();

    TestBed.resetTestingModule();
    storage.setItem(StorageKeys.SessionUser, JSON.stringify(RESPONSE.user));
    storage.setItem(StorageKeys.IdleTimeoutMinutes, 'abc');
    expect(setUp(storage).session()).toBeNull();

    TestBed.resetTestingModule();
    storage.removeItem(StorageKeys.RefreshToken);
    expect(setUp(storage).session()).toBeNull();
  });

  it('remembers the sign-in mode across a reload', () => {
    const store = setUp(storage);
    store.save(RESPONSE, SignInMode.Sso);
    expect(store.signInMode()).toBe(SignInMode.Sso);
    expect(storage.getItem(StorageKeys.SignInMode)).toBe(String(SignInMode.Sso));
    TestBed.resetTestingModule();
    expect(setUp(storage).signInMode()).toBe(SignInMode.Sso);
  });

  it('reads a session stored without a mode as a basic session', () => {
    setUp(storage).save(RESPONSE, SignInMode.ActiveDirectory);
    storage.removeItem(StorageKeys.SignInMode);
    TestBed.resetTestingModule();
    expect(setUp(storage).session()?.signInMode).toBe(SignInMode.Basic);
  });

  it('has no sign-in mode while signed out', () => {
    expect(setUp(storage).signInMode()).toBeNull();
  });

  it('clears memory and storage', () => {
    const store = setUp(storage);
    store.save(RESPONSE, SignInMode.Basic);
    store.clear();
    expect(store.session()).toBeNull();
    expect(storage.length).toBe(0);
  });

  describe('across tabs', () => {
    const ROTATED = { ...RESPONSE, accessToken: 'a2', refreshToken: 'r2', accessTokenExpiresAt: '2030-01-01T00:15:00Z' };

    /** What another tab's write looks like here: the storage changed, then a `storage` event fired. */
    function otherTab(write: () => void, key: string, newValue: string | null): void {
      write();
      window.dispatchEvent(new StorageEvent('storage', { key, newValue }));
    }

    function writeSession(target: Storage, response: typeof RESPONSE): void {
      target.setItem(StorageKeys.AccessToken, response.accessToken);
      target.setItem(StorageKeys.RefreshToken, response.refreshToken);
      target.setItem(StorageKeys.IdleTimeoutMinutes, String(response.idleTimeoutMinutes));
      target.setItem(StorageKeys.SessionUser, JSON.stringify(response.user));
      target.setItem(StorageKeys.AccessTokenExpiresAt, String(Date.parse(response.accessTokenExpiresAt)));
    }

    it('writes the expiry last and removes it last, so another tab never adopts half a session', () => {
      const writes: string[] = [];
      const recording = new MemoryStorage();
      const setItem = recording.setItem.bind(recording);
      const removeItem = recording.removeItem.bind(recording);
      recording.setItem = (key, value) => (writes.push(key), setItem(key, value));
      recording.removeItem = (key) => (writes.push(key), removeItem(key));
      const store = setUp(recording);
      store.save(RESPONSE, SignInMode.Basic);
      expect(writes.at(-1)).toBe(StorageKeys.AccessTokenExpiresAt);
      store.clear();
      expect(writes.at(-1)).toBe(StorageKeys.AccessTokenExpiresAt);
    });

    it('adopts tokens another tab rotated when their expiry is written', () => {
      const store = setUp(storage);
      store.save(RESPONSE, SignInMode.Basic);
      otherTab(() => writeSession(storage, ROTATED), StorageKeys.AccessTokenExpiresAt, 'x');
      expect(store.accessToken()).toBe('a2');
      expect(store.refreshToken()).toBe('r2');
    });

    it('ignores the events of the other session keys (they arrive before the session is complete)', () => {
      const store = setUp(storage);
      store.save(RESPONSE, SignInMode.Basic);
      otherTab(() => storage.setItem(StorageKeys.RefreshToken, 'r2'), StorageKeys.RefreshToken, 'r2');
      expect(store.refreshToken()).toBe('r1');
    });

    it('signs out when another tab clears the session', () => {
      const store = setUp(storage);
      store.save(RESPONSE, SignInMode.Basic);
      otherTab(() => storage.clear(), StorageKeys.AccessTokenExpiresAt, null);
      expect(store.session()).toBeNull();
    });

    it('adoptStored keeps the in-memory session when storage holds none (a storage that refuses writes)', () => {
      const store = setUp(storage);
      store.save(RESPONSE, SignInMode.Basic);
      storage.clear();
      expect(store.adoptStored()?.refreshToken).toBe('r1');
    });
  });
});
