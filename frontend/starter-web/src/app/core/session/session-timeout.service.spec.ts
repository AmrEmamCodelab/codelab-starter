import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { TokenStore, type StoredSession } from '../auth/token.store';
import { SessionEndReasons } from '../routing/route-paths';
import { BROWSER_STORAGE } from './app-storage';
import { APP_CLOCK } from './clock';
import { MemoryStorage } from './memory-storage';
import { ACTIVITY_WRITE_THROTTLE_MS, SessionTimeoutService } from './session-timeout.service';
import { StorageKeys } from './storage-keys';
import { MILLISECONDS_PER_MINUTE } from './time-units';

const IDLE_MINUTES = 2;
const IDLE_MS = IDLE_MINUTES * MILLISECONDS_PER_MINUTE;

describe('SessionTimeoutService', () => {
  const session = signal<StoredSession | null>(null);
  const expireSession = vi.fn().mockResolvedValue(undefined);
  let storage: MemoryStorage;

  function start(): SessionTimeoutService {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: BROWSER_STORAGE, useValue: storage },
        { provide: APP_CLOCK, useValue: () => Date.now() },
        { provide: TokenStore, useValue: { session } },
        { provide: AuthService, useValue: { expireSession } },
      ],
    });
    const service = TestBed.inject(SessionTimeoutService);
    service.start();
    TestBed.tick();
    return service;
  }

  function signIn(idleTimeoutMinutes = IDLE_MINUTES): void {
    session.set({ idleTimeoutMinutes } as StoredSession);
    TestBed.tick();
  }

  beforeEach(() => {
    vi.useFakeTimers();
    storage = new MemoryStorage();
    session.set(null);
    expireSession.mockClear();
  });

  afterEach(() => vi.useRealTimers());

  it('expires an idle session after the login response window', () => {
    start();
    signIn();
    vi.advanceTimersByTime(IDLE_MS - 1);
    expect(expireSession).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(expireSession).toHaveBeenCalledWith(SessionEndReasons.Idle, '/');
  });

  it('resets the window on user activity', () => {
    start();
    signIn();
    vi.advanceTimersByTime(IDLE_MS - 1);
    window.dispatchEvent(new Event('keydown'));
    vi.advanceTimersByTime(IDLE_MS - 1);
    expect(expireSession).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(expireSession).toHaveBeenCalledTimes(1);
  });

  it('publishes activity for other tabs, throttled', () => {
    start();
    signIn();
    const first = storage.getItem(StorageKeys.LastActivity);
    vi.advanceTimersByTime(ACTIVITY_WRITE_THROTTLE_MS - 1);
    window.dispatchEvent(new Event('pointermove'));
    expect(storage.getItem(StorageKeys.LastActivity)).toBe(first);
    vi.advanceTimersByTime(1);
    window.dispatchEvent(new Event('pointermove'));
    expect(storage.getItem(StorageKeys.LastActivity)).not.toBe(first);
  });

  it('stays alive while another tab is active', () => {
    start();
    signIn();
    vi.advanceTimersByTime(IDLE_MS - 1);
    storage.setItem(StorageKeys.LastActivity, String(Date.now()));
    vi.advanceTimersByTime(1);
    expect(expireSession).not.toHaveBeenCalled();
    vi.advanceTimersByTime(IDLE_MS + ACTIVITY_WRITE_THROTTLE_MS);
    expect(expireSession).toHaveBeenCalledTimes(1);
  });

  it('re-arms when another tab reports activity', () => {
    start();
    signIn();
    vi.advanceTimersByTime(IDLE_MS - 1);
    window.dispatchEvent(new StorageEvent('storage', { key: StorageKeys.LastActivity, newValue: '1' }));
    vi.advanceTimersByTime(IDLE_MS - 1);
    expect(expireSession).not.toHaveBeenCalled();
  });

  it('stops watching on sign-out and is disabled by a zero window', () => {
    start();
    signIn();
    session.set(null);
    TestBed.tick();
    vi.advanceTimersByTime(IDLE_MS * 2);
    signIn(0);
    vi.advanceTimersByTime(IDLE_MS * 2);
    expect(expireSession).not.toHaveBeenCalled();
  });

  it('starts once', () => {
    const service = start();
    service.start();
    signIn();
    vi.advanceTimersByTime(IDLE_MS);
    expect(expireSession).toHaveBeenCalledTimes(1);
  });
});
