import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { OPS_CONFIG } from '../config/ops-config';
import { TEST_CONFIG } from '../testing/ops.testing';
import { ApiService } from './api.service';
import { OpsApiClient, OpsApiPaths } from './ops-api.client';

describe('ApiService and OpsApiClient', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: OPS_CONFIG, useValue: TEST_CONFIG }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('resolves paths against the same-origin apiBaseUrl', () => {
    const api = TestBed.inject(ApiService);
    expect(api.url('health')).toBe('/ops-api/health');
    expect(api.url('/health')).toBe('/ops-api/health');
  });

  it('sends Windows credentials and no Authorization header', () => {
    TestBed.inject(ApiService).get('me').subscribe();
    const request = http.expectOne('/ops-api/me');
    expect(request.request.method).toBe('GET');
    expect(request.request.withCredentials).toBe(true);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ name: 'ops', groups: [] });
  });

  it.each([
    ['me', OpsApiPaths.Me],
    ['health', OpsApiPaths.Health],
    ['jobs', OpsApiPaths.Jobs],
    ['certificates', OpsApiPaths.Certificates],
  ] as const)('%s calls its BFF route', (method, path) => {
    let answer: unknown;
    TestBed.inject(OpsApiClient)[method]().subscribe((value) => (answer = value));
    http.expectOne(`/ops-api/${path}`).flush({ ok: true });
    expect(answer).toEqual({ ok: true });
  });

  it('URL-encodes the reference code into the path', () => {
    TestBed.inject(OpsApiClient).referenceCode('AB/C?D').subscribe();
    http.expectOne('/ops-api/reference-codes/AB%2FC%3FD').flush({});
  });
});
