import { HttpErrorResponse } from '@angular/common/http';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiErrorCodes, HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MeClient } from '../api/clients/me.client';
import type { PermissionsResponse, SessionResponse } from '../api/clients/api.models';
import { SignInMode } from '../auth/sign-in-mode';
import { TokenStore } from '../auth/token.store';
import { DEFAULT_PERMISSION_REFRESH_SECONDS } from '../config/app-config';
import { provideTestAppConfig, TEST_APP_CONFIG } from '../config/app-config.testing';
import { BROWSER_STORAGE } from '../session/app-storage';
import { APP_CLOCK } from '../session/clock';
import { MemoryStorage } from '../session/memory-storage';
import { PERMISSION_SYNC_THROTTLE_MS, PermissionSyncService } from './permission-sync.service';
import { PermissionStore } from './permission.store';

describe('PermissionSyncService', () => {
  let permissions: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    permissions = vi.fn().mockReturnValue(of({ permissions: ['dashboard.view'] }));
    TestBed.configureTestingModule({ providers: [{ provide: MeClient, useValue: { permissions } }] });
  });

  it('loads the keys into the store', async () => {
    await TestBed.inject(PermissionSyncService).refresh();
    expect(TestBed.inject(PermissionStore).has('dashboard.view')).toBe(true);
  });

  it('shares one request between concurrent refreshes', async () => {
    const pending = new Subject<PermissionsResponse>();
    permissions.mockReturnValue(pending);
    const sync = TestBed.inject(PermissionSyncService);
    const both = Promise.all([sync.refresh(), sync.refresh()]);
    pending.next({ permissions: ['a.b'] });
    pending.complete();
    await both;
    expect(permissions).toHaveBeenCalledTimes(1);
  });

  it('propagates a failed refresh and keeps the previous keys', async () => {
    const sync = TestBed.inject(PermissionSyncService);
    await sync.refresh();
    permissions.mockReturnValue(throwError(() => new Error('offline')));
    await expect(sync.refresh()).rejects.toThrow('offline');
    expect(TestBed.inject(PermissionStore).has('dashboard.view')).toBe(true);
  });

  it('ensureLoaded fetches once, then answers from the store', async () => {
    const sync = TestBed.inject(PermissionSyncService);
    await expect(sync.ensureLoaded()).resolves.toBe(true);
    await expect(sync.ensureLoaded()).resolves.toBe(true);
    expect(permissions).toHaveBeenCalledTimes(1);
  });

  it('ensureLoaded resolves false when the sync fails', async () => {
    permissions.mockReturnValue(throwError(() => new Error('offline')));
    await expect(TestBed.inject(PermissionSyncService).ensureLoaded()).resolves.toBe(false);
  });

  it('records why the sync failed (code and reference code), and forgets it after a good sync', async () => {
    const serverFault = new HttpErrorResponse({
      status: HttpStatus.serverError,
      error: { code: ApiErrorCodes.internalError, correlationId: 'abcde12345', referenceCode: 'ABCDE-12345' },
    });
    permissions.mockReturnValueOnce(throwError(() => serverFault));
    const sync = TestBed.inject(PermissionSyncService);
    await expect(sync.ensureLoaded()).resolves.toBe(false);
    expect(sync.lastFailure()).toMatchObject({ code: ApiErrorCodes.internalError, referenceCode: 'ABCDE-12345' });
    await expect(sync.ensureLoaded()).resolves.toBe(true);
    expect(sync.lastFailure()).toBeNull();
  });

  it('reads a non-HTTP sync failure as a server fault', async () => {
    permissions.mockReturnValue(throwError(() => new Error('offline')));
    const sync = TestBed.inject(PermissionSyncService);
    await sync.ensureLoaded();
    expect(sync.lastFailure()?.status).toBe(HttpStatus.serverError);
  });

  it('clear empties the store', async () => {
    const sync = TestBed.inject(PermissionSyncService);
    await sync.refresh();
    sync.clear();
    expect(TestBed.inject(PermissionStore).loaded()).toBe(false);
  });
});

@Component({ template: '' })
class BlankPage {}

const SESSION: SessionResponse = {
  accessToken: 'a1',
  refreshToken: 'r1',
  accessTokenExpiresAt: '2030-01-01T00:10:00Z',
  idleTimeoutMinutes: 0,
  user: { id: 'u1', displayName: 'User' },
};

describe('PermissionSyncService live refresh', () => {
  const INTERVAL_MS = DEFAULT_PERMISSION_REFRESH_SECONDS * 1000;
  let permissions: ReturnType<typeof vi.fn>;
  let now: number;

  function setUp(refreshSeconds = DEFAULT_PERMISSION_REFRESH_SECONDS): PermissionSyncService {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'page', component: BlankPage }, { path: 'other', component: BlankPage }]),
        provideTestAppConfig({ ...TEST_APP_CONFIG, permissionRefreshSeconds: refreshSeconds }),
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
        { provide: APP_CLOCK, useValue: () => now },
        { provide: MeClient, useValue: { permissions } },
      ],
    });
    return TestBed.inject(PermissionSyncService);
  }

  /** Signs in, loads the keys once (as login does) and starts the live triggers. */
  async function signedIn(refreshSeconds?: number): Promise<PermissionSyncService> {
    const sync = setUp(refreshSeconds);
    TestBed.inject(TokenStore).save(SESSION, SignInMode.Basic);
    sync.start();
    TestBed.tick();
    await sync.refresh();
    permissions.mockClear();
    return sync;
  }

  async function advance(ms: number): Promise<void> {
    now += ms;
    await vi.advanceTimersByTimeAsync(ms);
  }

  function setVisibility(state: DocumentVisibilityState): void {
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
    document.dispatchEvent(new Event('visibilitychange'));
  }

  beforeEach(() => {
    vi.useFakeTimers();
    now = Date.parse('2030-01-01T00:00:00Z');
    permissions = vi.fn().mockReturnValue(of({ permissions: ['dashboard.view'] }));
  });

  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(document, 'visibilityState');
  });

  it('syncs on the configured interval while signed in', async () => {
    await signedIn();
    await advance(INTERVAL_MS - 1);
    expect(permissions).not.toHaveBeenCalled();
    await advance(1);
    expect(permissions).toHaveBeenCalledTimes(1);
    await advance(INTERVAL_MS);
    expect(permissions).toHaveBeenCalledTimes(2);
  });

  it('applies a revoke and a grant to the store without a new login', async () => {
    await signedIn();
    const store = TestBed.inject(PermissionStore);
    permissions.mockReturnValue(of({ permissions: ['users.view'] }));
    await advance(INTERVAL_MS);
    expect(store.has('dashboard.view')).toBe(false);
    expect(store.has('users.view')).toBe(true);
  });

  it('runs no interval when it is configured as 0', async () => {
    await signedIn(0);
    await advance(INTERVAL_MS * 3);
    expect(permissions).not.toHaveBeenCalled();
  });

  it('syncs on window focus, throttled', async () => {
    await signedIn();
    await advance(PERMISSION_SYNC_THROTTLE_MS);
    window.dispatchEvent(new Event('focus'));
    expect(permissions).toHaveBeenCalledTimes(1);
    window.dispatchEvent(new Event('focus'));
    expect(permissions).toHaveBeenCalledTimes(1);
    await advance(PERMISSION_SYNC_THROTTLE_MS);
    window.dispatchEvent(new Event('focus'));
    expect(permissions).toHaveBeenCalledTimes(2);
  });

  it('does not sync on focus right after the login sync', async () => {
    await signedIn();
    window.dispatchEvent(new Event('focus'));
    expect(permissions).not.toHaveBeenCalled();
  });

  it('syncs when the tab becomes visible, not when it is hidden', async () => {
    await signedIn();
    await advance(PERMISSION_SYNC_THROTTLE_MS);
    setVisibility('hidden');
    expect(permissions).not.toHaveBeenCalled();
    setVisibility('visible');
    expect(permissions).toHaveBeenCalledTimes(1);
  });

  it('syncs after a navigation, collapsing a burst into one request', async () => {
    await signedIn();
    await advance(PERMISSION_SYNC_THROTTLE_MS);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/page');
    await router.navigateByUrl('/other');
    expect(permissions).toHaveBeenCalledTimes(1);
  });

  it('waits for the first sync of a session before syncing in the background', async () => {
    const sync = setUp();
    TestBed.inject(TokenStore).save(SESSION, SignInMode.Basic);
    sync.start();
    TestBed.tick();
    await advance(INTERVAL_MS);
    expect(permissions).not.toHaveBeenCalled();
  });

  it('stops every trigger when the session ends', async () => {
    await signedIn();
    TestBed.inject(TokenStore).clear();
    TestBed.tick();
    await advance(INTERVAL_MS * 2);
    window.dispatchEvent(new Event('focus'));
    expect(permissions).not.toHaveBeenCalled();
  });

  it('keeps the previous keys when a background sync fails, and retries on the next trigger', async () => {
    await signedIn();
    permissions.mockReturnValue(throwError(() => new HttpErrorResponse({ status: HttpStatus.serverError })));
    await advance(INTERVAL_MS);
    expect(TestBed.inject(PermissionStore).has('dashboard.view')).toBe(true);
    permissions.mockReturnValue(of({ permissions: [] }));
    await advance(INTERVAL_MS);
    expect(TestBed.inject(PermissionStore).has('dashboard.view')).toBe(false);
  });

  it('starts once', async () => {
    const sync = await signedIn();
    sync.start();
    TestBed.tick();
    await advance(INTERVAL_MS);
    expect(permissions).toHaveBeenCalledTimes(1);
  });
});
