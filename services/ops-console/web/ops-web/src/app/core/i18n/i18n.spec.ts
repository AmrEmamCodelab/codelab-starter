import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { STATIC_JSON_FETCH } from '../http/static-json.loader';
import { OPS_CONFIG } from '../config/ops-config';
import { TEST_CONFIG } from '../testing/ops.testing';
import { DateTimePipe, formatDateTime } from './date-time';
import { AppLanguages, directionOf, isAppLanguage, otherLanguage } from './language';
import { LANGUAGE_STORAGE_KEY, LanguageService } from './language.service';
import { TranslatePipe } from './translate.pipe';
import { TRANSLATIONS_PATH, TranslationService, flattenTranslations, interpolate } from './translation.service';

const BUNDLES: Record<string, unknown> = {
  [`${TRANSLATIONS_PATH}/ar.json`]: { app: { name: 'وحدة' }, greet: 'مرحبا {{name}}' },
  [`${TRANSLATIONS_PATH}/en.json`]: { app: { name: 'Console' }, greet: 'Hello {{name}}' },
};

function setUp(fetchJson = vi.fn(async (path: string) => BUNDLES[path])) {
  TestBed.configureTestingModule({
    providers: [
      { provide: STATIC_JSON_FETCH, useValue: fetchJson },
      { provide: OPS_CONFIG, useValue: TEST_CONFIG },
    ],
  });
  return fetchJson;
}

describe('language helpers', () => {
  it('knows the shipped languages, their direction and the toggle target', () => {
    expect(isAppLanguage('ar')).toBe(true);
    expect(isAppLanguage('fr')).toBe(false);
    expect(directionOf(AppLanguages.Arabic)).toBe('rtl');
    expect(directionOf(AppLanguages.English)).toBe('ltr');
    expect(otherLanguage(AppLanguages.Arabic)).toBe(AppLanguages.English);
    expect(otherLanguage(AppLanguages.English)).toBe(AppLanguages.Arabic);
  });
});

describe('flattenTranslations and interpolate', () => {
  it('flattens nested bundles into dotted keys', () => {
    expect(flattenTranslations({ a: { b: 'x', c: { d: 'y' } }, e: 'z' })).toEqual({ 'a.b': 'x', 'a.c.d': 'y', e: 'z' });
  });

  it('fills known placeholders and keeps unknown ones', () => {
    expect(interpolate('{{a}} and {{ b }} and {{c}}', { a: 1, b: 'two' })).toBe('1 and two and {{c}}');
    expect(interpolate('plain')).toBe('plain');
  });
});

describe('TranslationService', () => {
  it('loads a bundle once and translates with parameters', async () => {
    const fetchJson = setUp();
    const service = TestBed.inject(TranslationService);
    await service.use(AppLanguages.English);
    await service.use(AppLanguages.English);
    expect(service.translate('greet', { name: 'Sara' })).toBe('Hello Sara');
    expect(service.language()).toBe(AppLanguages.English);
    expect(fetchJson).toHaveBeenCalledTimes(1);
  });

  it('shows a missing key as the key itself', async () => {
    setUp();
    const service = TestBed.inject(TranslationService);
    await service.use(AppLanguages.Arabic);
    expect(service.translate('missing.key')).toBe('missing.key');
  });

  it('keeps keys visible when a bundle fails and retries on the next switch', async () => {
    const fetchJson = vi.fn<(path: string) => Promise<unknown>>().mockRejectedValueOnce(new Error('down'))
      .mockImplementation(async (path) => BUNDLES[path]);
    setUp(fetchJson);
    const service = TestBed.inject(TranslationService);
    await service.use(AppLanguages.English);
    expect(service.translate('app.name')).toBe('app.name');
    await service.use(AppLanguages.English);
    expect(service.translate('app.name')).toBe('Console');
  });

  it('feeds the t pipe', async () => {
    setUp();
    await TestBed.inject(TranslationService).use(AppLanguages.English);
    const pipe = TestBed.runInInjectionContext(() => new TranslatePipe());
    expect(pipe.transform('greet', { name: 'Omar' })).toBe('Hello Omar');
  });
});

describe('LanguageService', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => localStorage.clear());

  it('starts in the configured default language and sets lang and dir', async () => {
    setUp();
    const service = TestBed.inject(LanguageService);
    await service.init();
    const root = TestBed.inject(DOCUMENT).documentElement;
    expect(service.current()).toBe(AppLanguages.Arabic);
    expect(root.lang).toBe('ar');
    expect(root.dir).toBe('rtl');
    expect(service.alternate()).toBe(AppLanguages.English);
  });

  it('toggles, remembers the choice and switches the direction', async () => {
    setUp();
    const service = TestBed.inject(LanguageService);
    await service.toggle();
    expect(service.current()).toBe(AppLanguages.English);
    expect(service.dir()).toBe('ltr');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en');
  });

  it('prefers a remembered language and ignores a bad stored value', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en');
    setUp();
    expect(TestBed.inject(LanguageService).current()).toBe(AppLanguages.English);
    TestBed.resetTestingModule();
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'xx');
    setUp();
    expect(TestBed.inject(LanguageService).current()).toBe(AppLanguages.Arabic);
  });
});

describe('formatDateTime', () => {
  it('formats with Latin digits in both languages', () => {
    const english = formatDateTime('2026-11-07T10:15:00Z', AppLanguages.English);
    const arabic = formatDateTime('2026-11-07T10:15:00Z', AppLanguages.Arabic);
    expect(english).toMatch(/2026/);
    expect(arabic).toMatch(/2026/);
  });

  it.each([null, undefined, '', 'not a date'])('gives empty text for %s', (value) => {
    expect(formatDateTime(value, AppLanguages.English)).toBe('');
  });

  it('follows the active language in the pipe', async () => {
    setUp();
    await TestBed.inject(TranslationService).use(AppLanguages.English);
    const pipe = TestBed.runInInjectionContext(() => new DateTimePipe());
    expect(pipe.transform('2026-11-07T10:15:00Z')).toBe(formatDateTime('2026-11-07T10:15:00Z', AppLanguages.English));
  });
});
