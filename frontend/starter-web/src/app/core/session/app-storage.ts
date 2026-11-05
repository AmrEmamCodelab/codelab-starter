import { Injectable, InjectionToken, inject } from '@angular/core';
import type { StorageKey } from './storage-keys';

/** The browser storage the app persists to. Tests provide an in-memory `Storage`. */
export const BROWSER_STORAGE = new InjectionToken<Storage | null>('BROWSER_STORAGE', {
  providedIn: 'root',
  factory: () => (typeof localStorage === 'undefined' ? null : localStorage),
});

/**
 * Typed, fault-tolerant access to browser storage: only {@link StorageKey}s are accepted, and a storage
 * that refuses (private mode, quota, disabled) reads as empty and ignores writes instead of throwing,
 * so a storage fault degrades a convenience (remembered language, shared idle time) and never breaks a
 * screen.
 */
@Injectable({ providedIn: 'root' })
export class AppStorage {
  private readonly storage = inject(BROWSER_STORAGE);

  read(key: StorageKey): string | null {
    try {
      return this.storage?.getItem(key) ?? null;
    } catch {
      return null;
    }
  }

  write(key: StorageKey, value: string): void {
    try {
      this.storage?.setItem(key, value);
    } catch {
      // A refused write keeps the in-memory state; the value is simply not remembered.
    }
  }

  remove(key: StorageKey): void {
    try {
      this.storage?.removeItem(key);
    } catch {
      // Nothing stored that could be removed.
    }
  }
}
