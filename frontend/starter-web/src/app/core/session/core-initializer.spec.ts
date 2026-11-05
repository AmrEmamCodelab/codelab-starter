import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { FeatureStore } from '../features/feature.store';
import { LanguageService } from '../i18n/language.service';
import { ProtectedTranslationsService } from '../i18n/protected-translations.service';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { initializeCore } from './core-initializer';
import { SessionTimeoutService } from './session-timeout.service';

describe('initializeCore', () => {
  const authenticated = signal(false);
  const mocks = {
    init: vi.fn(),
    load: vi.fn(),
    ensureLoaded: vi.fn(),
    startTimeout: vi.fn(),
    startTranslations: vi.fn(),
  };

  beforeEach(() => {
    authenticated.set(false);
    Object.values(mocks).forEach(mock => mock.mockReset().mockResolvedValue(undefined));
    TestBed.configureTestingModule({
      providers: [
        { provide: LanguageService, useValue: { init: mocks.init } },
        { provide: FeatureStore, useValue: { load: mocks.load } },
        { provide: AuthService, useValue: { isAuthenticated: authenticated } },
        { provide: PermissionSyncService, useValue: { ensureLoaded: mocks.ensureLoaded } },
        { provide: SessionTimeoutService, useValue: { start: mocks.startTimeout } },
        { provide: ProtectedTranslationsService, useValue: { start: mocks.startTranslations } },
      ],
    });
  });

  it('applies the language, loads the flags and starts the watchers', async () => {
    await TestBed.runInInjectionContext(() => initializeCore());
    expect(mocks.init).toHaveBeenCalled();
    expect(mocks.load).toHaveBeenCalled();
    expect(mocks.startTimeout).toHaveBeenCalled();
    expect(mocks.startTranslations).toHaveBeenCalled();
    expect(mocks.ensureLoaded).not.toHaveBeenCalled();
  });

  it('syncs the permissions of a restored session', async () => {
    authenticated.set(true);
    await TestBed.runInInjectionContext(() => initializeCore());
    expect(mocks.ensureLoaded).toHaveBeenCalled();
  });
});
