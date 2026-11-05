import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { provideTestAppConfig, TEST_API_BASE_URL } from '../../config/app-config.testing';
import { SKIP_ERROR_DIALOG, SKIP_SESSION_EXPIRY } from '../../http/http-context';
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
  });

  it('logs out', () => {
    TestBed.inject(AuthClient).logout().subscribe();
    const request = http.expectOne(url(ApiPaths.AuthLogout));
    expect(request.request.method).toBe('POST');
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
  });

  it('reads the session signing key, handling its own failures', () => {
    TestBed.inject(AuthClient).signingKey().subscribe();
    const request = http.expectOne(url(ApiPaths.SessionSigningKey));
    expect(request.request.method).toBe('GET');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(request.request.context.get(SKIP_SESSION_EXPIRY)).toBe(true);
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
});
