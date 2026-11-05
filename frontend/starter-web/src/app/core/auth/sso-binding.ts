import { Injectable, InjectionToken, inject } from '@angular/core';
import { StorageKeys } from '../session/storage-keys';

/**
 * The SSO browser binding (login-CSRF defence). Each SSO start makes a fresh random secret that stays in THIS tab
 * (sessionStorage); the API receives only `base64url(SHA-256(secret))` as `binding` on `api/auth/sso/login`, and the
 * exchange must present the secret itself as `bindingProof`. A ticket link copied into another browser or tab has no
 * secret beside it, so it cannot be exchanged there. The API refuses a start without a binding and a ticket without
 * a matching proof.
 */

/** Random bytes in the secret: base64url gives 43 characters, inside the API's 32..256 proof bounds. */
export const SSO_BINDING_SECRET_BYTES = 32;

/** The digest the API compares the proof against. */
const BINDING_DIGEST = 'SHA-256';

/** The Web Crypto API the binding uses. Tests may provide their own. */
export const BROWSER_CRYPTO = new InjectionToken<Crypto | null>('BROWSER_CRYPTO', {
  providedIn: 'root',
  factory: () => (typeof globalThis.crypto === 'undefined' ? null : globalThis.crypto),
});

/** The per-tab storage the secret lives in (never localStorage: the binding belongs to one tab). */
export const TAB_STORAGE = new InjectionToken<Storage | null>('TAB_STORAGE', {
  providedIn: 'root',
  factory: () => (typeof sessionStorage === 'undefined' ? null : sessionStorage),
});

/** A fresh binding: the secret kept in the tab and the hash sent to the API. */
export interface SsoBinding {
  readonly secret: string;
  readonly hash: string;
}

/** Base64url without padding (RFC 4648 section 5), as the API's `Base64Url` encodes. */
export function base64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** Makes a random secret and its base64url SHA-256 over the secret's UTF-8 bytes. */
export async function createSsoBinding(cryptoApi: Crypto): Promise<SsoBinding> {
  const secret = base64Url(cryptoApi.getRandomValues(new Uint8Array(SSO_BINDING_SECRET_BYTES)));
  const digest = await cryptoApi.subtle.digest(BINDING_DIGEST, new TextEncoder().encode(secret));
  return { secret, hash: base64Url(new Uint8Array(digest)) };
}

/** Keeps the binding secret of the SSO sign-in this tab started, until the callback takes it (once). */
@Injectable({ providedIn: 'root' })
export class SsoBindingStore {
  private readonly cryptoApi = inject(BROWSER_CRYPTO);
  private readonly storage = inject(TAB_STORAGE);

  /**
   * Starts a binding: stores a fresh secret in this tab and returns its hash for `api/auth/sso/login`.
   *
   * @returns null when the browser has no Web Crypto or refuses tab storage (the SSO start then fails visibly).
   */
  async begin(): Promise<string | null> {
    if (this.cryptoApi === null) {
      return null;
    }
    const binding = await createSsoBinding(this.cryptoApi);
    try {
      this.storage?.setItem(StorageKeys.SsoBindingSecret, binding.secret);
    } catch {
      return null;
    }
    return this.storage === null ? null : binding.hash;
  }

  /** The secret this tab holds, read once (it is removed). Null when this tab started no SSO sign-in. */
  take(): string | null {
    try {
      const secret = this.storage?.getItem(StorageKeys.SsoBindingSecret) ?? null;
      this.storage?.removeItem(StorageKeys.SsoBindingSecret);
      return secret === '' ? null : secret;
    } catch {
      return null;
    }
  }
}
