import { HttpErrorResponse, HttpStatusCode } from '@angular/common/http';
import { ErrorHandler } from '@angular/core';
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
import { BrowserLocation } from '../session/browser-location';
import { MemoryStorage } from '../session/memory-storage';
import { StorageKeys } from '../session/storage-keys';
import { ACCESS_TOKEN_REFRESH_LEAD_MS, AuthService } from './auth.service';
import { SignInMode } from './sign-in-mode';
import { TAB_STORAGE } from './sso-binding';
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

const END_SESSION_URL = 'https://idp.test/end-session?id_token_hint=h&post_logout_redirect_uri=x';
const SSO_LOGIN_URL = 'http://api.test/api/auth/sso/login';

describe('AuthService', () => {
  let client: {
    login: ReturnType<typeof vi.fn>;
    refresh: ReturnType<typeof vi.fn>;
    logout: ReturnType<typeof vi.fn>;
    signingKey: ReturnType<typeof vi.fn>;
    directoryLogin: ReturnType<typeof vi.fn>;
    ssoExchange: ReturnType<typeof vi.fn>;
    ssoLogout: ReturnType<typeof vi.fn>;
    ssoLoginUrl: ReturnType<typeof vi.fn>;
  };
  let browser: { assign: ReturnType<typeof vi.fn> };
  let sync: { refresh: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  let keys: { adopt: ReturnType<typeof vi.fn>; clear: ReturnType<typeof vi.fn> };
  let storage: MemoryStorage;
  let tabStorage: MemoryStorage;

  function setUp(): AuthService {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: BROWSER_STORAGE, useValue: storage },
        { provide: TAB_STORAGE, useValue: tabStorage },
        { provide: APP_CLOCK, useValue: () => NOW },
        { provide: AuthClient, useValue: client },
        { provide: PermissionSyncService, useValue: sync },
        { provide: SessionKeyStore, useValue: keys },
        { provide: BrowserLocation, useValue: browser },
      ],
    });
    return TestBed.inject(AuthService);
  }

  beforeEach(() => {
    vi.useFakeTimers();
    storage = new MemoryStorage();
    tabStorage = new MemoryStorage();
    client = {
      login: vi.fn().mockReturnValue(of(response('a1'))),
      refresh: vi.fn().mockReturnValue(of(response('a2'))),
      logout: vi.fn().mockReturnValue(of(undefined)),
      signingKey: vi.fn().mockReturnValue(throwError(() => new HttpErrorResponse({ status: HttpStatusCode.NotFound }))),
      directoryLogin: vi.fn().mockReturnValue(of(response('ad1'))),
      ssoExchange: vi.fn().mockReturnValue(of(response('sso1'))),
      ssoLogout: vi.fn().mockReturnValue(of(END_SESSION_URL)),
      ssoLoginUrl: vi.fn((returnUrl: string | null, binding: string) => `${SSO_LOGIN_URL}?returnUrl=${returnUrl ?? ''}&binding=${binding}`),
    };
    browser = { assign: vi.fn().mockReturnValue(true) };
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

  it('expires the session with a visible reason and reports the failure when the refresh is refused', async () => {
    const refusal = new Error('401');
    client.refresh.mockReturnValue(throwError(() => refusal));
    const auth = setUp();
    await auth.login('user', 'secret');
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const report = vi.spyOn(TestBed.inject(ErrorHandler), 'handleError').mockImplementation(() => undefined);
    await expect(auth.refresh()).resolves.toBe(false);
    expect(auth.isAuthenticated()).toBe(false);
    expect(sync.clear).toHaveBeenCalled();
    expect(keys.clear).toHaveBeenCalled();
    expect(report).toHaveBeenCalledWith(refusal);
    expect(String(navigate.mock.calls[0]?.[0])).toContain(`reason=${SessionEndReasons.Expired}`);
  });

  it('expires the session when the signing keys of the refreshed session are refused', async () => {
    const auth = setUp();
    await auth.login('user', 'secret');
    client.refresh.mockReturnValue(of(response('a2', SIGNING)));
    keys.adopt.mockRejectedValue(new Error('refused'));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(ErrorHandler), 'handleError').mockImplementation(() => undefined);
    await expect(auth.refresh()).resolves.toBe(false);
    expect(auth.isAuthenticated()).toBe(false);
    expect(String(navigate.mock.calls[0]?.[0])).toContain(`reason=${SessionEndReasons.Expired}`);
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
    TestBed.inject(TokenStore).save(response('restored'), SignInMode.Basic);
    TestBed.resetTestingModule();
    setUp();
    await vi.advanceTimersByTimeAsync(TEN_MINUTES_MS);
    expect(client.refresh).toHaveBeenCalledWith('restored-refresh');
  });
  describe('sign-in methods', () => {
    it('signs in with Active Directory and records the mode', async () => {
      const auth = setUp();
      await auth.loginWithDirectory('user', 'secret');
      expect(client.directoryLogin).toHaveBeenCalledWith({ userName: 'user', password: 'secret' });
      expect(client.login).not.toHaveBeenCalled();
      expect(TestBed.inject(TokenStore).signInMode()).toBe(SignInMode.ActiveDirectory);
      expect(sync.refresh).toHaveBeenCalled();
    });

    it('records a basic login as basic', async () => {
      await setUp().login('user', 'secret');
      expect(TestBed.inject(TokenStore).signInMode()).toBe(SignInMode.Basic);
    });

    it('starts SSO with a top-level navigation, the binding hash and the safe return URL', async () => {
      const auth = setUp();
      await expect(auth.startSsoSignIn('/admin')).resolves.toBe(true);
      const secret = tabStorage.getItem(StorageKeys.SsoBindingSecret);
      expect(secret).toMatch(/^[A-Za-z0-9_-]{43}$/);
      const binding = client.ssoLoginUrl.mock.calls[0]?.[1] as string;
      expect(binding).toMatch(/^[A-Za-z0-9_-]{43}$/);
      expect(binding).not.toBe(secret);
      expect(client.ssoLoginUrl).toHaveBeenCalledWith('/admin', binding);
      expect(browser.assign).toHaveBeenCalledWith(`${SSO_LOGIN_URL}?returnUrl=/admin&binding=${binding}`);
      expect(storage.getItem(StorageKeys.SsoReturnUrl)).toBe('/admin');
      expect(storage.getItem(StorageKeys.SsoBindingSecret)).toBeNull();
    });

    it('hands the binding secret out once, as the exchange proof', async () => {
      const auth = setUp();
      await auth.startSsoSignIn(null);
      const secret = tabStorage.getItem(StorageKeys.SsoBindingSecret);
      expect(auth.takeSsoBindingProof()).toBe(secret);
      expect(auth.takeSsoBindingProof()).toBeNull();
    });

    it('never sends or remembers an off-site return URL', async () => {
      const auth = setUp();
      storage.setItem(StorageKeys.SsoReturnUrl, '/old');
      await auth.startSsoSignIn('https://evil.test/');
      expect(client.ssoLoginUrl).toHaveBeenCalledWith(null, expect.any(String));
      expect(storage.getItem(StorageKeys.SsoReturnUrl)).toBeNull();
    });

    it('reports a refused SSO navigation', async () => {
      browser.assign.mockReturnValue(false);
      await expect(setUp().startSsoSignIn(null)).resolves.toBe(false);
    });

    it('swaps the SSO ticket and the binding proof for a session in SSO mode', async () => {
      const auth = setUp();
      await auth.completeSsoSignIn('ticket-1', 'proof-1');
      expect(client.ssoExchange).toHaveBeenCalledWith({ ticket: 'ticket-1', bindingProof: 'proof-1' });
      expect(TestBed.inject(TokenStore).signInMode()).toBe(SignInMode.Sso);
      expect(auth.isAuthenticated()).toBe(true);
    });

    it('stores nothing when the SSO exchange is refused', async () => {
      client.ssoExchange.mockReturnValue(throwError(() => new HttpErrorResponse({ status: HttpStatusCode.BadRequest })));
      const auth = setUp();
      await expect(auth.completeSsoSignIn('spent', 'proof-1')).rejects.toBeInstanceOf(HttpErrorResponse);
      expect(auth.isAuthenticated()).toBe(false);
    });

    it('hands the remembered return URL out once', () => {
      const auth = setUp();
      storage.setItem(StorageKeys.SsoReturnUrl, '/admin');
      expect(auth.takeSsoReturnUrl()).toBe('/admin');
      expect(auth.takeSsoReturnUrl()).toBeNull();
    });

    it('rechecks a remembered return URL', () => {
      storage.setItem(StorageKeys.SsoReturnUrl, '//evil.test');
      expect(setUp().takeSsoReturnUrl()).toBeNull();
    });

    it('keeps the sign-in mode when the token is refreshed', async () => {
      const auth = setUp();
      await auth.completeSsoSignIn('ticket-1', 'proof-1');
      await auth.refresh();
      expect(TestBed.inject(TokenStore).accessToken()).toBe('a2');
      expect(TestBed.inject(TokenStore).signInMode()).toBe(SignInMode.Sso);
    });
  });

  describe('SSO logout', () => {
    it('ends the SSO session and navigates the whole page to the end-session URL', async () => {
      const auth = setUp();
      await auth.completeSsoSignIn('ticket-1', 'proof-1');
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await auth.logout();
      expect(client.ssoLogout).toHaveBeenCalled();
      expect(client.logout).not.toHaveBeenCalled();
      expect(auth.isAuthenticated()).toBe(false);
      expect(browser.assign).toHaveBeenCalledWith(END_SESSION_URL);
      expect(navigate).not.toHaveBeenCalled();
    });

    it('goes to the login page when the API answers 204 (no end-session URL)', async () => {
      client.ssoLogout.mockReturnValue(of(null));
      const auth = setUp();
      await auth.completeSsoSignIn('ticket-1', 'proof-1');
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await auth.logout();
      expect(browser.assign).not.toHaveBeenCalled();
      expect(String(navigate.mock.calls[0]?.[0])).toBe('/login');
    });

    it('goes to the login page when the browser seam refuses the end-session URL', async () => {
      const auth = setUp();
      await auth.completeSsoSignIn('ticket-1', 'proof-1');
      browser.assign.mockReturnValue(false);
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await auth.logout();
      expect(String(navigate.mock.calls[0]?.[0])).toBe('/login');
    });

    it('clears the local session when the SSO logout fails', async () => {
      client.ssoLogout.mockReturnValue(throwError(() => new Error('gone')));
      const auth = setUp();
      await auth.completeSsoSignIn('ticket-1', 'proof-1');
      vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await auth.logout();
      expect(auth.isAuthenticated()).toBe(false);
      expect(browser.assign).not.toHaveBeenCalled();
    });

    it('logs an Active Directory session out through the plain logout', async () => {
      const auth = setUp();
      await auth.loginWithDirectory('user', 'secret');
      vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
      await auth.logout();
      expect(client.logout).toHaveBeenCalled();
      expect(client.ssoLogout).not.toHaveBeenCalled();
    });
  });
});
