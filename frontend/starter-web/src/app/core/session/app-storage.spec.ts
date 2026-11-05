import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AppStorage, BROWSER_STORAGE } from './app-storage';
import { MemoryStorage } from './memory-storage';
import { StorageKeys } from './storage-keys';

class RefusingStorage extends MemoryStorage {
  override getItem(): string | null {
    throw new Error('denied');
  }
  override setItem(): void {
    throw new Error('quota');
  }
  override removeItem(): void {
    throw new Error('denied');
  }
}

function setUp(storage: Storage | null): AppStorage {
  TestBed.configureTestingModule({ providers: [{ provide: BROWSER_STORAGE, useValue: storage }] });
  return TestBed.inject(AppStorage);
}

describe('AppStorage', () => {
  it('reads, writes and removes typed keys', () => {
    const storage = setUp(new MemoryStorage());
    storage.write(StorageKeys.Language, 'en');
    expect(storage.read(StorageKeys.Language)).toBe('en');
    storage.remove(StorageKeys.Language);
    expect(storage.read(StorageKeys.Language)).toBeNull();
  });

  it('degrades to empty when the storage refuses', () => {
    const storage = setUp(new RefusingStorage());
    expect(() => storage.write(StorageKeys.Language, 'en')).not.toThrow();
    expect(() => storage.remove(StorageKeys.Language)).not.toThrow();
    expect(storage.read(StorageKeys.Language)).toBeNull();
  });

  it('works without any storage', () => {
    const storage = setUp(null);
    storage.write(StorageKeys.Language, 'en');
    expect(storage.read(StorageKeys.Language)).toBeNull();
  });

  it('defaults to localStorage', () => {
    expect(TestBed.inject(BROWSER_STORAGE)).toBe(localStorage);
  });

  it('memory storage implements Storage', () => {
    const memory = new MemoryStorage();
    memory.setItem('a', '1');
    expect(memory.key(0)).toBe('a');
    expect(memory.key(1)).toBeNull();
    memory.clear();
    expect(memory.length).toBe(0);
  });
});
