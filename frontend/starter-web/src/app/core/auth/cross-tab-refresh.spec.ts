import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { EnvironmentInjector, createEnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthClient } from '../api/clients/auth.client';
import type { SessionResponse } from '../api/clients/api.models';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { AppStorage, BROWSER_STORAGE } from '../session/app-storage';
import { APP_CLOCK } from '../session/clock';
import { MemoryStorage } from '../session/memory-storage';
import { ACCESS_TOKEN_REFRESH_LEAD_MS, AuthService } from './auth.service';
import { CROSS_TAB_LOCK, noCrossTabLock, type CrossTabLock } from './cross-tab-lock';
import { SignInMode } from './sign-in-mode';
import { TokenStore } from './token.store';

const NOW = Date.parse('2030-01-01T00:00:00Z');
const TOKEN_LIFETIME_MS = 900_000;

/** One shared lock for every tab: tasks run one after another, like `navigator.locks`. */
function serialLock(): CrossTabLock {
  let tail: Promise<unknown> = Promise.resolve();
  return <T>(_name: string, task: () => Promise<T>): Promise<T> => {
    const run = tail.then(task, task);
    tail = run.catch(() => undefined);
    return run;
  };
}

/**
 * The server's refresh rule: the refresh token is single-use, and sending a rotated one again revokes
 * the session (reuse detection), after which every refresh is refused.
 */
class FakeServer {
  private generation = 1;
  revoked = false;
  readonly refresh = vi.fn((refreshToken: string) => {
    if (this.revoked || refreshToken !== this.currentRefreshToken()) {
      this.revoked = true;
      return throwError(() => new HttpErrorResponse({ status: HttpStatusCode.BadRequest }));
    }
    this.generation += 1;
    return of(this.session());
  });

  session(): SessionResponse {
    return {
      accessToken: `a${this.generation}`,
      refreshToken: this.currentRefreshToken(),
      accessTokenExpiresAt: new Date(Date.now() + TOKEN_LIFETIME_MS).toISOString(),
      idleTimeoutMinutes: 15,
      user: { id: 'u1', displayName: 'User' },
    };
  }

  private currentRefreshToken(): string {
    return `r${this.generation}`;
  }
}

describe('AuthService across two tabs sharing one storage', () => {
  let server: FakeServer;
  let storage: MemoryStorage;
  const tabs: EnvironmentInjector[] = [];

  function openTab(lock: CrossTabLock): EnvironmentInjector {
    const tab = createEnvironmentInjector(
      [AppStorage, TokenStore, AuthService, { provide: CROSS_TAB_LOCK, useValue: lock }],
      TestBed.inject(EnvironmentInjector),
    );
    tabs.push(tab);
    tab.get(AuthService);
    return tab;
  }

  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    server = new FakeServer();
    storage = new MemoryStorage();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: BROWSER_STORAGE, useValue: storage },
        { provide: APP_CLOCK, useValue: () => Date.now() },
        { provide: AuthClient, useValue: { refresh: server.refresh } },
        { provide: PermissionSyncService, useValue: { refresh: vi.fn(), clear: vi.fn() } },
      ],
    });
    // Tab one signed in earlier; both tabs start from the stored session.
    TestBed.inject(TokenStore).save(server.session(), SignInMode.Basic);
  });

  afterEach(() => {
    tabs.splice(0).forEach(tab => tab.destroy());
    vi.useRealTimers();
  });

  it('rotates once when both tabs reach the refresh time together, and keeps both signed in', async () => {
    const lock = serialLock();
    const first = openTab(lock);
    const second = openTab(lock);

    await vi.advanceTimersByTimeAsync(TOKEN_LIFETIME_MS - ACCESS_TOKEN_REFRESH_LEAD_MS);

    expect(server.refresh).toHaveBeenCalledTimes(1);
    expect(server.revoked).toBe(false);
    expect(first.get(TokenStore).refreshToken()).toBe('r2');
    expect(second.get(TokenStore).refreshToken()).toBe('r2');
    expect(second.get(AuthService).isAuthenticated()).toBe(true);
  });

  it('adopts the tokens another tab rotated instead of re-sending the old refresh token', async () => {
    const first = openTab(noCrossTabLock);
    const second = openTab(noCrossTabLock);

    await expect(first.get(AuthService).refresh()).resolves.toBe(true);
    await expect(second.get(AuthService).refresh()).resolves.toBe(true);

    expect(server.refresh).toHaveBeenCalledTimes(1);
    expect(server.revoked).toBe(false);
    expect(second.get(TokenStore).accessToken()).toBe('a2');
  });
});
