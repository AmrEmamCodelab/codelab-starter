/**
 * Where the session key store persists: a synchronous marker with the token's lifetime, and a key
 * record store that can hold `CryptoKey` handles. Both are ports so the store is testable without a
 * browser; the defaults below use `localStorage` and IndexedDB.
 *
 * Why two places. The marker separates "the server is not asking for proofs" (no signing block was
 * ever delivered: pass requests through) from "a signing session whose key has gone missing"
 * (IndexedDB evicted while `localStorage` survived: fail closed). Without it the interceptor would
 * have to guess, and both guesses are defects.
 *
 * Why IndexedDB. It is the only browser store that holds a `CryptoKey`; `localStorage` holds
 * strings, which would mean raw key bytes beside the bearer token.
 */

/** The "this session was issued signing material" flag. Synchronous: it is read on every request. */
export interface SigningSessionMarker {
  /** True when set. Must not throw: an unreadable marker reads as absent. */
  isSet(): boolean;
  /** Sets the marker. Throws when storage refuses it. */
  set(): void;
  /** Clears the marker. Must not throw. */
  clear(): void;
}

/** A single-record store that can persist `CryptoKey` handles. */
export interface KeyRecordStore {
  /** Whether the store exists in this environment. */
  isAvailable(): boolean;
  /** Replaces the record. */
  write(record: unknown): Promise<void>;
  /** The record, or null when absent or unreadable. Never throws. */
  read(): Promise<unknown>;
  /** Deletes the record, best effort. Never throws. */
  erase(): Promise<void>;
}

/** The names the default persistence uses. Per origin; the starter's defaults are below. */
export interface SessionPersistenceNames {
  readonly markerKey: string;
  readonly databaseName: string;
  readonly storeName: string;
  readonly recordKey: string;
}

export const DEFAULT_SESSION_PERSISTENCE_NAMES: SessionPersistenceNames = Object.freeze({
  markerKey: 'codelab_signing_session',
  databaseName: 'codelab-security',
  storeName: 'session-keys',
  recordKey: 'active',
});

const MARKER_VALUE = '1';

/** The default marker, in `localStorage` (the token's lifetime, not IndexedDB's). */
export function localStorageMarker(markerKey: string): SigningSessionMarker {
  return {
    isSet: () => {
      try {
        return globalThis.localStorage?.getItem(markerKey) === MARKER_VALUE;
      } catch (error) {
        // A locked-down profile can throw on access: absent marker means no signing session.
        return reportUnreadable(error);
      }
    },
    set: () => {
      if (globalThis.localStorage === undefined) throw new Error('localStorage is not available.');
      globalThis.localStorage.setItem(markerKey, MARKER_VALUE);
    },
    clear: () => {
      try {
        globalThis.localStorage?.removeItem(markerKey);
      } catch (error) {
        reportUnreadable(error);
      }
    },
  };
}

function reportUnreadable(error: unknown): false {
  console.warn('The signing-session marker is not readable; treating it as absent.', error);
  return false;
}

const DATABASE_VERSION = 1;
const READ_ONLY: IDBTransactionMode = 'readonly';
const READ_WRITE: IDBTransactionMode = 'readwrite';

/** The default key record store, in IndexedDB. Every connection is closed after one request. */
export function indexedDbKeyRecordStore(
  names: Pick<SessionPersistenceNames, 'databaseName' | 'storeName' | 'recordKey'>,
): KeyRecordStore {
  const run = async <T>(mode: IDBTransactionMode, action: (store: IDBObjectStore) => IDBRequest) => {
    const db = await openDatabase(names.databaseName, names.storeName);
    try {
      return await runRequest<T>({ db, storeName: names.storeName, mode, action });
    } finally {
      db.close();
    }
  };
  return {
    isAvailable: () => typeof globalThis.indexedDB !== 'undefined',
    write: async (record) => {
      await run(READ_WRITE, (store) => store.put(record, names.recordKey));
    },
    read: () => run<unknown>(READ_ONLY, (store) => store.get(names.recordKey)).then((value) => value ?? null, () => null),
    erase: () => run(READ_WRITE, (store) => store.delete(names.recordKey)).then(() => undefined, () => undefined),
  };
}

function openDatabase(databaseName: string, storeName: string): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const request = globalThis.indexedDB.open(databaseName, DATABASE_VERSION);
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(storeName)) request.result.createObjectStore(storeName);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('IndexedDB open failed.'));
    // Rejecting rather than hanging: a hung promise here would stall every API call behind it.
    request.onblocked = () => reject(new Error('IndexedDB open was blocked by another connection.'));
  });
}

interface StoreRequest {
  readonly db: IDBDatabase;
  readonly storeName: string;
  readonly mode: IDBTransactionMode;
  readonly action: (store: IDBObjectStore) => IDBRequest;
}

function runRequest<T>(request: StoreRequest): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const transaction = request.db.transaction(request.storeName, request.mode);
    const pending = request.action(transaction.objectStore(request.storeName));
    pending.onerror = () => reject(pending.error ?? new Error('IndexedDB request failed.'));
    transaction.oncomplete = () => resolve(pending.result as T);
    transaction.onabort = () => reject(transaction.error ?? new Error('IndexedDB transaction aborted.'));
  });
}
