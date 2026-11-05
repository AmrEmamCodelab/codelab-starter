import { signal, type Provider, type WritableSignal } from '@angular/core';
import { vi, type Mock } from 'vitest';
import type { SessionUser } from '../core/api/clients/api.models';
import { AuthService } from '../core/auth/auth.service';
import { AppLanguages, type AppLanguage } from '../core/i18n/language';
import { LanguageService } from '../core/i18n/language.service';
import { TranslationService } from '../core/i18n/translation.service';

/**
 * Test doubles shared by the layout and feature specs (spec-only; nothing in the app imports this file).
 * Translations echo their key, so assertions read the key a template asked for.
 */
export interface FakeAuth {
  readonly user: WritableSignal<SessionUser | null>;
  readonly isAuthenticated: WritableSignal<boolean>;
  readonly logout: Mock<() => Promise<void>>;
  readonly login: Mock<(userName: string, password: string) => Promise<void>>;
}

export function fakeAuth(user: SessionUser | null = { id: 'user-public-id', displayName: 'Test User' }): FakeAuth {
  return {
    user: signal(user),
    isAuthenticated: signal(user !== null),
    logout: vi.fn<() => Promise<void>>().mockResolvedValue(undefined),
    login: vi.fn<(userName: string, password: string) => Promise<void>>().mockResolvedValue(undefined),
  };
}

export interface FakeLanguage {
  readonly current: WritableSignal<AppLanguage>;
  readonly set: Mock<(language: AppLanguage) => Promise<void>>;
}

export function fakeLanguage(current: AppLanguage = AppLanguages.Arabic): FakeLanguage {
  const language = signal(current);
  return {
    current: language,
    set: vi.fn((next: AppLanguage) => {
      language.set(next);
      return Promise.resolve();
    }),
  };
}

/** Renders `key` (and `key|name=value` for parameters) instead of a translation. */
export function echoTranslate(key: string, params?: Readonly<Record<string, string | number>>): string {
  if (!params) {
    return key;
  }
  return `${key}|${Object.entries(params).map(([name, value]) => `${name}=${value}`).join(',')}`;
}

export function layoutTestProviders(auth: FakeAuth, language: FakeLanguage = fakeLanguage()): Provider[] {
  return [
    { provide: AuthService, useValue: auth },
    { provide: LanguageService, useValue: language },
    { provide: TranslationService, useValue: { translate: echoTranslate } },
  ];
}
