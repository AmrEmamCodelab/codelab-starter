import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { STATIC_JSON_FETCH } from '../http/static-json.loader';
import { flattenTranslations, interpolate, PUBLIC_TRANSLATIONS_PATH, TranslationService } from './translation.service';
import { TranslatePipe } from './translate.pipe';

const BUNDLES: Record<string, unknown> = {
  [`${PUBLIC_TRANSLATIONS_PATH}/ar.json`]: { app: { title: 'عنوان', greet: 'مرحبا {{name}}' } },
  [`${PUBLIC_TRANSLATIONS_PATH}/en.json`]: { app: { title: 'Title', greet: 'Hello {{name}}' } },
};

describe('flattenTranslations', () => {
  it('flattens nested objects into dotted keys and skips non-strings', () => {
    const tree = { a: { b: 'x', c: { d: 'y' } }, e: 'z', n: null } as never;
    expect(flattenTranslations(tree)).toEqual({ 'a.b': 'x', 'a.c.d': 'y', e: 'z' });
  });
});

describe('interpolate', () => {
  it('replaces known placeholders and keeps unknown ones', () => {
    expect(interpolate('Hi {{ name }} {{other}}', { name: 'Sara' })).toBe('Hi Sara {{other}}');
    expect(interpolate('plain')).toBe('plain');
  });
});

describe('TranslationService', () => {
  let fetchJson: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchJson = vi.fn((path: string) =>
      path in BUNDLES ? Promise.resolve(BUNDLES[path]) : Promise.reject(new Error('404')));
    TestBed.configureTestingModule({ providers: [{ provide: STATIC_JSON_FETCH, useValue: fetchJson }] });
  });

  it('loads the public bundle on first use and translates', async () => {
    const service = TestBed.inject(TranslationService);
    await service.use('en');
    expect(service.language()).toBe('en');
    expect(service.translate('app.title')).toBe('Title');
    expect(service.translate('app.greet', { name: 'Sara' })).toBe('Hello Sara');
    expect(service.has('app.title')).toBe(true);
  });

  it('renders a missing key as the key', async () => {
    const service = TestBed.inject(TranslationService);
    await service.use('ar');
    expect(service.translate('missing.key')).toBe('missing.key');
    expect(service.has('missing.key')).toBe(false);
  });

  it('loads each bundle once', async () => {
    const service = TestBed.inject(TranslationService);
    await service.use('en');
    await service.use('ar');
    await service.use('en');
    expect(fetchJson).toHaveBeenCalledTimes(2);
  });

  it('retries a bundle that failed to load', async () => {
    fetchJson.mockRejectedValueOnce(new Error('offline'));
    const service = TestBed.inject(TranslationService);
    await service.use('en');
    expect(service.translate('app.title')).toBe('app.title');
    await service.use('en');
    expect(service.translate('app.title')).toBe('Title');
  });

  it('merges a further bundle over the public one', async () => {
    const service = TestBed.inject(TranslationService);
    await service.use('en');
    service.merge('en', { app: { title: 'Gated title' }, admin: { heading: 'Admin' } });
    expect(service.translate('app.title')).toBe('Gated title');
    expect(service.translate('admin.heading')).toBe('Admin');
    expect(service.translate('app.greet', { name: 'x' })).toBe('Hello x');
  });

  it('feeds the t pipe', async () => {
    const service = TestBed.inject(TranslationService);
    await service.use('en');
    const pipe = TestBed.runInInjectionContext(() => new TranslatePipe());
    expect(pipe.transform('app.greet', { name: 'Omar' })).toBe('Hello Omar');
  });
});
