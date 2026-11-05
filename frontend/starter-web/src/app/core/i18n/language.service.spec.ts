import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { provideTestAppConfig, TEST_APP_CONFIG } from '../config/app-config.testing';
import { BROWSER_STORAGE } from '../session/app-storage';
import { MemoryStorage } from '../session/memory-storage';
import { StorageKeys } from '../session/storage-keys';
import { AppLanguages, directionOf, isAppLanguage, TextDirections } from './language';
import { LanguageService } from './language.service';
import { TranslationService } from './translation.service';

describe('language helpers', () => {
  it('knows the supported languages and their direction', () => {
    expect(isAppLanguage('ar')).toBe(true);
    expect(isAppLanguage('fr')).toBe(false);
    expect(isAppLanguage(1)).toBe(false);
    expect(directionOf(AppLanguages.Arabic)).toBe(TextDirections.RightToLeft);
    expect(directionOf(AppLanguages.English)).toBe(TextDirections.LeftToRight);
  });
});

describe('LanguageService', () => {
  let storage: MemoryStorage;
  let use: ReturnType<typeof vi.fn>;

  function setUp(config = TEST_APP_CONFIG): LanguageService {
    TestBed.configureTestingModule({
      providers: [
        provideTestAppConfig(config),
        { provide: BROWSER_STORAGE, useValue: storage },
        { provide: TranslationService, useValue: { use } },
      ],
    });
    return TestBed.inject(LanguageService);
  }

  beforeEach(() => {
    storage = new MemoryStorage();
    use = vi.fn().mockResolvedValue(undefined);
  });

  it('defaults to the configured language (Arabic, right-to-left)', async () => {
    const service = setUp();
    await service.init();
    const root = TestBed.inject(DOCUMENT).documentElement;
    expect(service.current()).toBe('ar');
    expect(service.dir()).toBe('rtl');
    expect(root.lang).toBe('ar');
    expect(root.dir).toBe('rtl');
    expect(use).toHaveBeenCalledWith('ar');
  });

  it('honours a different configured default', () => {
    expect(setUp({ ...TEST_APP_CONFIG, defaultLanguage: 'en' }).current()).toBe('en');
  });

  it('switches, applies and remembers the choice', async () => {
    const service = setUp();
    await service.set('en');
    expect(service.current()).toBe('en');
    expect(service.dir()).toBe('ltr');
    expect(TestBed.inject(DOCUMENT).documentElement.dir).toBe('ltr');
    expect(storage.getItem(StorageKeys.Language)).toBe('en');
  });

  it('restores a remembered choice and ignores an unknown one', () => {
    storage.setItem(StorageKeys.Language, 'en');
    expect(setUp().current()).toBe('en');
    TestBed.resetTestingModule();
    storage.setItem(StorageKeys.Language, 'xx');
    expect(setUp().current()).toBe('ar');
  });
});
