import { signal, type Provider, type WritableSignal } from '@angular/core';
import type { Routes } from '@angular/router';
import { vi, type Mock } from 'vitest';
import type { SessionUser } from '../core/api/clients/api.models';
import { AuthService } from '../core/auth/auth.service';
import { AppLanguages, TextDirections, type AppLanguage, type TextDirection } from '../core/i18n/language';
import { LanguageService } from '../core/i18n/language.service';
import { TranslationService } from '../core/i18n/translation.service';
import { APP_CLOCK } from '../core/session/clock';
import type { NotificationItem } from './notifications-menu/notifications';

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

/** 2026-11-05T09:30:00Z: a fixed "now" for the footer year and anything else that reads the clock. */
export const TEST_NOW = Date.UTC(2026, 10, 5, 9, 30);

/** Pins `APP_CLOCK` to `now`. */
export function fixedClock(now: number = TEST_NOW): Provider {
  return { provide: APP_CLOCK, useValue: () => now };
}

/** A notification with sensible defaults; override any field. */
export function aNotification(overrides: Partial<NotificationItem> = {}): NotificationItem {
  return {
    id: 'notification-1',
    titleText: 'Title',
    bodyText: 'Body',
    at: '2026-11-05T09:30:00Z',
    read: false,
    ...overrides,
  };
}

/** Both document directions, for specs that run once in RTL (Arabic, the default) and once in LTR. */
export const BOTH_DIRECTIONS: readonly TextDirection[] = [TextDirections.RightToLeft, TextDirections.LeftToRight];

/** Sets `<html dir>` as `LanguageService` would; returns a function that restores the previous value. */
export function useDirection(direction: TextDirection): () => void {
  const root = document.documentElement;
  const previous = root.getAttribute('dir');
  root.setAttribute('dir', direction);
  return () => (previous === null ? root.removeAttribute('dir') : root.setAttribute('dir', previous));
}

/** A route table that accepts every URL, so a spec can follow a link without a "no route" rejection. */
export const ANY_URL_ROUTES: Routes = [{ path: '**', children: [] }];
