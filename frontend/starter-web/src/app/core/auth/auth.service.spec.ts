import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SessionKeyStore, type SigningSessionBlock } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthClient } from '../api/clients/auth.client';
import type { SessionResponse } from '../api/clients/api.models';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { SessionEndReasons } from '../routing/route-paths';
import { BROWSER_STORAGE } from '../session/app-storage';
import { APP_CLOCK } from '../session/clock';
import { MemoryStorage } from '../session/memory-storage';
import { ACCESS_TOKEN_REFRESH_LEAD_MS, AuthService } from './auth.service';
import { TokenStore } from './token.store';

const NOW = Date.parse('2030-01-01T00:00:00Z');
const TEN_MINUTES_MS = 600_000;

function response(accessToken: string, signing?: SigningSessionBlock): SessionResponse {
  return {
    accessToken,
    refreshToken: `${accessToken}-refresh`,
    accessTokenExpiresAt: new Date(NOW + TEN_MINUTES_MS).toISOString(),
    idleTimeoutMinutes: 15,
    user: { id: 'u1', displayName: 'User' },
    ...(signing ? { signing } : {}),
  };
}

const SIGNING: SigningSessionBlock = {
  alg: 'HMAC-SHA256',
  sessionSigningKey: 'key',
  audience: 'aud',
  verifyResponses: true,
  serverTimeUnix: 1,
};

describe('AuthService', () => {
  let client: {
    login: ReturnType<typeof vi.fn>;
    refresh: ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
    signingKey: ReturnType<typeof vi.fn>;
  };
  let sync: { refresh: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  let keys: { adopt: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  let storage: MemoryStorage;

  function setUp(): AuthService {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: BROWSER_STORAGE, useValue: storage },
        { provide: APP_CLOCK, useValue: () => NOW },
        { provide: AuthClient, useValue: client },
        { provide: PermissionSyncService, useValue: sync },
        { provide: SessionKeyStore, useValue: keys },
      ],
    });
    return TestBed.inject(AuthService);
  }

  beforeEach(() => {
    vi.useFakeTimers();
    storage = new MemoryStorage();
    client = {
      login: vi.fn().mockReturnValue(of(response('a1'))),
      refresh: vi.fn().mockReturnValue(of(response('a2'))),
      logout: vi.fn().mockReturnValue(of(undefined)),
      signingKey: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: HttpStatusCode.NotFound }))),
    };
    sync = { refresh: vi.fn().mockResolvedValue(undefined), clear: vi.fn() };
    keys = { adopt: vi.fn().mockResolvedValue({}), clear: vi.fn().mockResolvedValue(undefined) };
  });

  afterEach(() => vi.useRealTimers());

  it('logs in, stores the session and syncs permissions', async () => {
    const auth = setUp();
    await auth.login('user', 'secret');
    expect(client.login).toHaveBeenCalledWith({ userName: 'user', password: 'secret' });
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.user()?.displayName).toBe('User');
    expect(sync.refresh).toHaveBeenCalled();
    expect(keys.adopt).not.toHaveBeenCalled();
  });

  it('adopts the signing keys when the server sends them', async () => {
    client.login.mockReturnValue(of(response('a1', SIGNING)));
    await setUp().login('user', 'secret');
    expect(keys.adopt).toHaveBeenCalledWith(SIGNING);
  });

  it('fetches the signing keys when the login response carries none', async () => {
    client.signingKey.mockReturnValue(of(SIGNING));
    await setUp().login('user', 'secret');
    expect(client.signingKey).toHaveBeenCalled();
    expect(keys.adopt).toHaveBeenCalledWith(SIGNING);
  });

  it('does not fetch the signing keys when the login response carries them', async () => {
    client.login.mockReturnValue(of(response('a1', SIGNING)));
    await setUp().login('user', 'secret');
    expect(client.signingKey).not.toHaveBeenCalled();
  });

  it('stores nothing when the signing-key request fails for another reason than 404', async () => {
    client.signingKey.mockReturnValue(throwError(() => new HttpErrorResponse({ status: HttpStatusCode.Unauthorized })));
    const auth = setUp();
    await expect(auth.login('user', 'secret')).rejects.toBeInstanceOf(HttpErrorResponse);
    expect(auth.isAuthenticated()).toBe(false);
    expect(keys.adopt).not.toHaveBeenCalled();
  });

  it('stores nothing when login fails', async () => {
    client.login.mockReturnValue(throwError(() => new Error('refused')));
    const auth = setUp();
    await expect(auth.login('user', 'bad')).rejects.toThrow('refused');
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('stores nothing when the signing keys are refused', async () => {
    client.login.mockReturnValue(of(response('a1', SIGNING)));
    keys.adopt.mockRejectedValue(new Error('signing_material_rejected'));
    const auth = setUp();
    await expect(auth.login('user', 'secret')).rejects.toThrow('signing_material_rejected');
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('stays signed in when the permission sync fails', async () => {
    sync.refresh.mockRejectedValue(new Error('offline'));
    const auth = setUp();
    await auth.login('user', 'secret');
    expect(auth.isAuthenticated()).toBe(true);
  });

  it('refreshes the token one minute before it expires', async () => {
    const auth = setUp();
    await auth.login('user', 'secret');
    await vi.advanceTimersByTimeAsync(TEN_MINUTES_MS - ACCESS_TOKEN_REFRESH_LEAD_MS - 1);
    expect(client.refresh).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1);
    expect(client.refresh).toHaveBeenCalledWith('a1-refresh');
    expect(TestBed.inject(TokenStore).accessToken()).toBe('a2');
  });

  it('shares one request between concurrent refreshes', async () => {
    const auth = setUp();
    await auth.login('user', 'secret');
    const [first, second] = await Promise.all([auth.refresh(), auth.refresh()]);
    expect(first && second).toBe(true);
    expect(client.refresh).toHaveBeenCalledTimes(1);
  });

  it('ends the session when the refresh is refused', async () => {
    client.refresh.mockReturnValue(throwError(() => new Error('401')));
    const auth = setUp();
    await auth.login('user', 'secret');
    await expect(auth.refresh()).resolves.toBe(false);
    expect(auth.isAuthenticated()).toBe(false);
    expect(sync.clear).toHaveBeenCalled();
    expect(keys.clear).toHaveBeenCalled();
  });

  it('does not refresh without a refresh token', async () => {
    await expect(setUp().refresh()).resolves.toBe(false);
    expect(client.refresh).not.toHaveBeenCalled();
  });

  it('logs out on the server, clears the session and goes to login', async () => {
    const auth = setUp();
    await auth.login('user', 'secret');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await auth.logout();
    expect(client.logout).toHaveBeenCalled();
    expect(auth.isAuthenticated()).toBe(false);
    expect(String(navigate.mock.calls[0]?.[0])).toBe('/login');
  });

  it('clears the local session even when the server logout fails', async () => {
    client.logout.mockReturnValue(throwError(() => new Error('gone')));
    const auth = setUp();
    await auth.login('user', 'secret');
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await auth.logout();
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('skips the server call when logging out without a session', async () => {
    const auth = setUp();
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await auth.logout();
    expect(client.logout).not.toHaveBeenCalled();
  });

  it('expires a session with a reason and the return URL', async () => {
    const auth = setUp();
    await auth.login('user', 'secret');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    await auth.expireSession(SessionEndReasons.Idle, '/admin');
    expect(auth.isAuthenticated()).toBe(false);
    expect(String(navigate.mock.calls[0]?.[0])).toBe('/login?returnUrl=%2Fadmin&reason=idle');
  });

  it('schedules a refresh for a restored session', async () => {
    setUp();
    TestBed.inject(TokenStore).save(response('restored'));
    TestBed.resetTestingModule();
    setUp();
    await vi.advanceTimersByTimeAsync(TEN_MINUTES_MS);
    expect(client.refresh).toHaveBeenCalledWith('restored-refresh');
  });
});
