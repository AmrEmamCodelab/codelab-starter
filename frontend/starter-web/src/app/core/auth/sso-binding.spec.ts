import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { MemoryStorage } from '../session/memory-storage';
import { StorageKeys } from '../session/storage-keys';
import { BROWSER_CRYPTO, SsoBindingStore, TAB_STORAGE, base64Url, createSsoBinding } from './sso-binding';

const BASE64URL_SHA256 = /^[A-Za-z0-9_-]{43}$/;

/** base64url(SHA-256(proof)) computed independently, as the API's SsoBinding.HashOf does. */
async function expectedHash(proof: string): Promise<string> {
  const digest = await globalThis.crypto.subtle.digest('SHA-256', new TextEncoder().encode(proof));
  return Buffer.from(digest).toString('base64url');
}

function setUp(storage: Storage | null, cryptoApi: Crypto | null = globalThis.crypto): SsoBindingStore {
  TestBed.configureTestingModule({
    providers: [
      { provide: TAB_STORAGE, useValue: storage },
      { provide: BROWSER_CRYPTO, useValue: cryptoApi },
    ],
  });
  return TestBed.inject(SsoBindingStore);
}

describe('the SSO browser binding', () => {
  it('encodes base64url without padding', () => {
    expect(base64Url(new Uint8Array([0xfb, 0xff, 0xfe]))).toBe('-__-');
    expect(base64Url(new Uint8Array([0x61]))).toBe('YQ');
  });

  it('makes a 43-character secret whose hash is the base64url SHA-256 of its UTF-8 bytes', async () => {
    const binding = await createSsoBinding(globalThis.crypto);
    expect(binding.secret).toMatch(BASE64URL_SHA256);
    expect(binding.hash).toMatch(BASE64URL_SHA256);
    expect(binding.hash).toBe(await expectedHash(binding.secret));
  });

  it('makes a different secret every time', async () => {
    const first = await createSsoBinding(globalThis.crypto);
    const second = await createSsoBinding(globalThis.crypto);
    expect(first.secret).not.toBe(second.secret);
  });

  it('keeps the secret in tab storage and hands it out once', async () => {
    const storage = new MemoryStorage();
    const store = setUp(storage);
    const hash = await store.begin();
    const secret = storage.getItem(StorageKeys.SsoBindingSecret);
    expect(secret).not.toBeNull();
    expect(hash).toBe(await expectedHash(secret ?? ''));
    expect(store.take()).toBe(secret);
    expect(store.take()).toBeNull();
  });

  it('reads as no binding when this tab started no sign-in', () => {
    expect(setUp(new MemoryStorage()).take()).toBeNull();
  });

  it('cannot start without tab storage or Web Crypto', async () => {
    await expect(setUp(null).begin()).resolves.toBeNull();
    TestBed.resetTestingModule();
    await expect(setUp(new MemoryStorage(), null).begin()).resolves.toBeNull();
  });
});
