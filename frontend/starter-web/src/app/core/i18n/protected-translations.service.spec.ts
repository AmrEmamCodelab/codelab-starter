import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TranslationsClient } from '../api/clients/translations.client';
import { TokenStore, type StoredSession } from '../auth/token.store';
import { FeatureStore } from '../features/feature.store';
import type { AppLanguage } from './language';
import { LanguageService } from './language.service';
import { ProtectedTranslationsService } from './protected-translations.service';
import { TranslationService } from './translation.service';

const SESSION = { accessToken: 'a1' } as StoredSession;

describe('ProtectedTranslationsService', () => {
  const session = signal<StoredSession | null>(null);
  const language = signal<AppLanguage>('ar');
  const enabled = signal(true);
  let protectedBundle: ReturnType<typeof vi.fn>;
  let merge: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    session.set(null);
    language.set('ar');
    enabled.set(true);
    protectedBundle = vi.fn().mockReturnValue(of({ admin: { title: 'x' } }));
    merge = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: TranslationsClient, useValue: { protectedBundle } },
        { provide: TokenStore, useValue: { session } },
        { provide: FeatureStore, useValue: { isEnabled: () => enabled() } },
        { provide: LanguageService, useValue: { current: language } },
        { provide: TranslationService, useValue: { merge } },
      ],
    });
  });

  it('merges the gated bundle once per language and session', async () => {
    const service = TestBed.inject(ProtectedTranslationsService);
    await service.load('ar', 'a1');
    await service.load('ar', 'a1');
    expect(protectedBundle).toHaveBeenCalledTimes(1);
    expect(merge).toHaveBeenCalledWith('ar', { admin: { title: 'x' } });
  });

  it('fetches again for a new session', async () => {
    const service = TestBed.inject(ProtectedTranslationsService);
    await service.load('ar', 'a1');
    await service.load('ar', 'a2');
    expect(protectedBundle).toHaveBeenCalledTimes(2);
  });

  it('swallows a failure and retries next time', async () => {
    protectedBundle.mockReturnValueOnce(throwError(() => new Error('404')));
    const service = TestBed.inject(ProtectedTranslationsService);
    await service.load('en', 'a1');
    expect(merge).not.toHaveBeenCalled();
    await service.load('en', 'a1');
    expect(merge).toHaveBeenCalledTimes(1);
  });

  it('follows the session, the language and the flag once started', async () => {
    const service = TestBed.inject(ProtectedTranslationsService);
    service.start();
    service.start();
    TestBed.tick();
    expect(protectedBundle).not.toHaveBeenCalled();

    session.set(SESSION);
    TestBed.tick();
    expect(protectedBundle).toHaveBeenCalledWith('ar');

    language.set('en');
    TestBed.tick();
    expect(protectedBundle).toHaveBeenCalledWith('en');
  });

  it('stays off while the flag is off', () => {
    enabled.set(false);
    session.set(SESSION);
    TestBed.inject(ProtectedTranslationsService).start();
    TestBed.tick();
    expect(protectedBundle).not.toHaveBeenCalled();
  });
});
