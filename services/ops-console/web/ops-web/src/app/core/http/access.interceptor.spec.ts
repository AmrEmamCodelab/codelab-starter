import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RoutePaths } from '../routing/route-paths';
import { accessInterceptor, accessPageFor } from './access.interceptor';

describe('accessPageFor', () => {
  it('maps 401 and 403 to the access pages and leaves other failures to the screen', () => {
    expect(accessPageFor(new HttpErrorResponse({ status: 401 }))).toBe(RoutePaths.SignInRequired);
    expect(accessPageFor(new HttpErrorResponse({ status: 403 }))).toBe(RoutePaths.AccessDenied);
    expect(accessPageFor(new HttpErrorResponse({ status: 404 }))).toBeNull();
    expect(accessPageFor(new Error('boom'))).toBeNull();
  });
});

describe('accessInterceptor', () => {
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([accessInterceptor])), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  function call(status: number): unknown {
    let failure: unknown;
    TestBed.inject(HttpClient).get('/ops-api/me').subscribe({ error: (error: unknown) => (failure = error) });
    http.expectOne('/ops-api/me').flush(null, { status, statusText: 'x' });
    return failure;
  }

  it.each([
    [401, RoutePaths.SignInRequired],
    [403, RoutePaths.AccessDenied],
  ])('moves to the access page on %i and still fails the call', (status, page) => {
    const failure = call(status);
    expect(navigate).toHaveBeenCalledWith([page]);
    expect((failure as HttpErrorResponse).status).toBe(status);
  });

  it('does not navigate for other failures', () => {
    call(500);
    expect(navigate).not.toHaveBeenCalled();
  });
});
