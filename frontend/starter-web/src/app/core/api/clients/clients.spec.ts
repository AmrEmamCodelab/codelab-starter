import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { provideTestAppConfig, TEST_API_BASE_URL } from '../../config/app-config.testing';
import { SKIP_ERROR_DIALOG, SKIP_SESSION_EXPIRY, UNSIGNED_REQUEST } from '../../http/http-context';
import { BROWSER_STORAGE } from '../../session/app-storage';
import { MemoryStorage } from '../../session/memory-storage';
import { AUTHORIZATION_HEADER } from '../api.service';
import { AccessClient } from './access.client';
import { ApiPaths } from './api-paths';
import { AuthClient } from './auth.client';
import { DashboardClient } from './dashboard.client';
import { FeaturesClient } from './features.client';
import { MeClient } from './me.client';
import { TranslationsClient } from './translations.client';

const url = (path: string) => `${TEST_API_BASE_URL}/${path}`;

describe('core API clients', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideTestAppConfig(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_STORAGE, useValue: new MemoryStorage() },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('logs in anonymously and handles its own failures', () => {
    TestBed.inject(AuthClient).login({ userName: 'u', password: 'p' }).subscribe();
    const request = http.expectOne(url(ApiPaths.AuthLogin));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ userName: 'u', password: 'p' });
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
  });

  it('refreshes with the refresh token', () => {
    TestBed.inject(AuthClient).refresh('r1').subscribe();
    const request = http.expectOne(url(ApiPaths.AuthRefresh));
    expect(request.request.body).toEqual({ refreshToken: 'r1' });
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
    expect(request.request.context.get(UNSIGNED_REQUEST)).toBe(true);
  });

  it('logs out', () => {
    TestBed.inject(AuthClient).logout().subscribe();
    const request = http.expectOne(url(ApiPaths.AuthLogout));
    expect(request.request.method).toBe('POST');
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
  });

  it('reads the session signing key with the given access token, handling its own failures', () => {
    TestBed.inject(AuthClient).signingKey('access-new').subscribe();
    const request = http.expectOne(url(ApiPaths.SessionSigningKey));
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.get(AUTHORIZATION_HEADER)).toBe('Bearer access-new');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
    expect(request.request.context.get(UNSIGNED_REQUEST)).toBe(true);
  });

  it('reads my permissions quietly', () => {
    TestBed.inject(MeClient).permissions().subscribe();
    const request = http.expectOne(url(ApiPaths.MePermissions));
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(false);
  });

  it('reads the feature flags anonymously', () => {
    TestBed.inject(FeaturesClient).flags().subscribe();
    expect(http.expectOne(url(ApiPaths.Features)).request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
  });

  it('reads the dashboard and the access admin placeholder', () => {
    TestBed.inject(DashboardClient).get().subscribe();
    TestBed.inject(AccessClient).admin().subscribe();
    http.expectOne(url(ApiPaths.Dashboard));
    http.expectOne(url(ApiPaths.AccessAdmin));
  });

  it('reads the protected translations for a language', () => {
    TestBed.inject(TranslationsClient).protectedBundle('en').subscribe();
    http.expectOne(`${url(ApiPaths.ProtectedTranslations)}?lang=en`);
  });

  it('keeps every path relative and under the API prefix', () => {
    for (const path of Object.values(ApiPaths)) {
      expect(path.startsWith('api/')).toBe(true);
    }
  });

  it('reads the sign-in modes anonymously', () => {
    TestBed.inject(AuthClient).modes().subscribe();
    const request = http.expectOne(url(ApiPaths.AuthModes));
    expect(request.request.method).toBe('GET');
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });

  it('signs in with Active Directory anonymously', () => {
    TestBed.inject(AuthClient).directoryLogin({ userName: 'u', password: 'p' }).subscribe();
    const request = http.expectOne(url(ApiPaths.AuthAdLogin));
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ userName: 'u', password: 'p' });
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
  });

  it('builds the absolute SSO start URL with an encoded return URL and the binding hash', () => {
    const client = TestBed.inject(AuthClient);
    expect(client.ssoLoginUrl(null, 'h-1')).toBe(`${url(ApiPaths.AuthSsoLogin)}?binding=h-1`);
    expect(client.ssoLoginUrl('/admin/users?page=2&q=a b', 'h-1')).toBe(
      `${url(ApiPaths.AuthSsoLogin)}?returnUrl=%2Fadmin%2Fusers%3Fpage%3D2%26q%3Da+b&binding=h-1`,
    );
  });

  it('exchanges the SSO ticket anonymously with the binding proof', () => {
    TestBed.inject(AuthClient).ssoExchange({ ticket: 't1', bindingProof: 'p1' }).subscribe();
    const request = http.expectOne(url(ApiPaths.AuthSsoExchange));
    expect(request.request.body).toEqual({ ticket: 't1', bindingProof: 'p1' });
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
  });

  it('activates an account anonymously', () => {
    TestBed.inject(AuthClient).activate({ ticket: 't1', password: 'p' }).subscribe();
    const request = http.expectOne(url(ApiPaths.AuthActivate));
    expect(request.request.body).toEqual({ ticket: 't1', password: 'p' });
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
  });

  it('ends an SSO session and reads the end-session URL, or null for a 204', () => {
    const results: (string | null)[] = [];
    const client = TestBed.inject(AuthClient);
    client.ssoLogout().subscribe(value => results.push(value));
    const first = http.expectOne(url(ApiPaths.AuthSsoLogout));
    expect(first.request.method).toBe('POST');
    expect(first.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
    first.flush({ endSessionUrl: 'https://idp.test/end' });
    client.ssoLogout().subscribe(value => results.push(value));
    http.expectOne(url(ApiPaths.AuthSsoLogout)).flush(null, { status: 204, statusText: 'No Content' });
    expect(results).toEqual(['https://idp.test/end', null]);
  });
});
