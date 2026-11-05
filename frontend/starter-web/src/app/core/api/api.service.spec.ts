import { HttpParams, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { provideTestAppConfig, TEST_API_BASE_URL } from '../config/app-config.testing';
import { quietContext, SKIP_ERROR_DIALOG, UNSIGNED_REQUEST } from '../http/http-context';
import { BROWSER_STORAGE } from '../session/app-storage';
import { MemoryStorage } from '../session/memory-storage';
import { SignInMode } from '../auth/sign-in-mode';
import { TokenStore } from '../auth/token.store';
import { ApiService, AUTHORIZATION_HEADER } from './api.service';

const SESSION = {
  accessToken: 'access-1',
  refreshToken: 'refresh-1',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  idleTimeoutMinutes: 15,
  user: { id: 'u1', displayName: 'User' },
};

describe('ApiService', () => {
  let api: ApiService;
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
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  it('resolves paths against the configured API root', () => {
    expect(api.url('api/x')).toBe(`${TEST_API_BASE_URL}/api/x`);
  });

  it('sends no Authorization header without a session', () => {
    api.get('api/x').subscribe();
    const request = http.expectOne(`${TEST_API_BASE_URL}/api/x`);
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    request.flush({});
  });

  it('attaches the bearer token of the current session', () => {
    TestBed.inject(TokenStore).save(SESSION, SignInMode.Basic);
    api.post('api/x', { a: 1 }).subscribe();
    const request = http.expectOne(`${TEST_API_BASE_URL}/api/x`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ a: 1 });
    expect(request.request.headers.get(AUTHORIZATION_HEADER)).toBe('Bearer access-1');
    request.flush({});
  });

  it('omits the token for anonymous calls and passes params, headers and context', () => {
    TestBed.inject(TokenStore).save(SESSION, SignInMode.Basic);
    api
      .get('api/x', {
        anonymous: true,
        params: new HttpParams().set('q', '1'),
        headers: { 'X-Extra': 'y' },
        context: quietContext(),
      })
      .subscribe();
    const request = http.expectOne(`${TEST_API_BASE_URL}/api/x?q=1`);
    expect(request.request.headers.has(AUTHORIZATION_HEADER)).toBe(false);
    expect(request.request.headers.get('X-Extra')).toBe('y');
    expect(request.request.context.get(SKIP_ERROR_DIALOG)).toBe(true);
    request.flush({});
  });

  it('sends anonymous calls unsigned without changing the caller context', () => {
    const context = quietContext();
    api.post('api/x', null, { anonymous: true, context }).subscribe();
    api.post('api/y', null).subscribe();
    expect(http.expectOne(`${TEST_API_BASE_URL}/api/x`).request.context.get(UNSIGNED_REQUEST)).toBe(true);
    expect(http.expectOne(`${TEST_API_BASE_URL}/api/y`).request.context.get(UNSIGNED_REQUEST)).toBe(false);
    expect(context.get(UNSIGNED_REQUEST)).toBe(false);
  });

  it('issues PUT and DELETE', () => {
    api.put('api/x', { b: 2 }).subscribe();
    api.delete('api/y').subscribe();
    expect(http.expectOne(`${TEST_API_BASE_URL}/api/x`).request.method).toBe('PUT');
    expect(http.expectOne(`${TEST_API_BASE_URL}/api/y`).request.method).toBe('DELETE');
  });
});
