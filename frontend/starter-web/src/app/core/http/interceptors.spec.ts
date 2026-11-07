import { HttpClient, HttpContext, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { ResponseSignatureError, ResponseSignatureFailures } from '@codelab/ng-security';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { PermissionSyncService } from '../permissions/permission-sync.service';
import { SessionEndReasons } from '../routing/route-paths';
import { errorDialogInterceptor } from './error-dialog.interceptor';
import { ErrorDialogStore } from './error-dialog.store';
import { forbiddenInterceptor, isForbidden } from './forbidden.interceptor';
import { quietContext, selfHandledContext } from './http-context';
import { sessionExpiredInterceptor } from './session-expired.interceptor';

const URL = 'http://api.test/api/x';
const TRACE_ID = 'd1e8a7c45b2f4e6a9c3b0f7d5a2e8c61';
const PROBLEM = { code: 'BUSINESS_RULE_VIOLATED', correlationId: 'abc123', referenceCode: 'REF-1' };

describe('core response interceptors', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let dialog: ErrorDialogStore;
  let navigate: ReturnType<typeof vi.spyOn>;
  const authenticated = signal(true);
  const expireSession = vi.fn().mockResolvedValue(undefined);
  const refresh = vi.fn().mockResolvedValue(undefined);

  beforeEach(() => {
    authenticated.set(true);
    expireSession.mockClear();
    refresh.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([sessionExpiredInterceptor, forbiddenInterceptor, errorDialogInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isAuthenticated: authenticated, expireSession } },
        { provide: PermissionSyncService, useValue: { refresh } },
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    dialog = TestBed.inject(ErrorDialogStore);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  });

  function fail(status: number, context?: HttpContext, body: object = PROBLEM): unknown {
    let received: unknown;
    http.get(URL, { context }).subscribe({ error: (error: unknown) => (received = error) });
    backend.expectOne(URL).flush(body, { status, statusText: 'x' });
    return received;
  }

  describe('errorDialogInterceptor', () => {
    it('shows a parsed problem and rethrows it', () => {
      expect(fail(422)).toBeDefined();
      expect(dialog.current()?.code).toBe('BUSINESS_RULE_VIOLATED');
      expect(dialog.messageKey()).toBe('errors.BUSINESS_RULE_VIOLATED');
      expect(dialog.referenceCode()).toBe('REF-1');
    });

    it('falls back to the X-Correlation-ID header for the reference code when the body has none', () => {
      http.get(URL).subscribe({ error: () => undefined });
      backend
        .expectOne(URL)
        .flush('', { status: 404, statusText: 'x', headers: { 'X-Correlation-ID': TRACE_ID } });
      expect(dialog.referenceCode()).toBe('D1E8A-7C45B');
      expect(dialog.correlationId()).toBe(TRACE_ID);
    });

    it('keeps the body reference code over the header', () => {
      http.get(URL).subscribe({ error: () => undefined });
      backend.expectOne(URL).flush(PROBLEM, { status: 409, statusText: 'x', headers: { 'X-Correlation-ID': TRACE_ID } });
      expect(dialog.referenceCode()).toBe('REF-1');
    });

    it('stays closed when the caller handles its own failures', () => {
      fail(500, quietContext());
      expect(dialog.current()).toBeNull();
    });

    it.each([401, 403])('leaves %s to the other interceptors', status => {
      fail(status);
      expect(dialog.current()).toBeNull();
    });

    it('passes successes through', () => {
      http.get(URL).subscribe();
      backend.expectOne(URL).flush({});
      expect(dialog.current()).toBeNull();
    });

    it('dismisses', () => {
      fail(500);
      dialog.dismiss();
      expect(dialog.current()).toBeNull();
      expect(dialog.messageKey()).toBeNull();
    });
  });

  describe('forbiddenInterceptor', () => {
    it('re-syncs the permissions and opens the 403 page', () => {
      fail(403);
      expect(refresh).toHaveBeenCalled();
      expect(String(navigate.mock.calls[0]?.[0])).toBe('/unauthorized');
    });

    it('leaves a self-handled 403 alone', () => {
      fail(403, quietContext());
      expect(navigate).not.toHaveBeenCalled();
    });

    it('treats a 403 whose signature could not be verified as a 403, and nothing else', () => {
      const unverified = (status: number) =>
        new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status, url: URL });
      expect(isForbidden(unverified(403))).toBe(true);
      expect(isForbidden(unverified(500))).toBe(false);
      expect(isForbidden(new HttpErrorResponse({ status: 403 }))).toBe(true);
      expect(isForbidden(new HttpErrorResponse({ status: 404 }))).toBe(false);
      expect(isForbidden(new Error('403'))).toBe(false);
    });
  });

  describe('sessionExpiredInterceptor', () => {
    it('ends the session on a 401', () => {
      fail(401);
      expect(expireSession).toHaveBeenCalledWith(SessionEndReasons.Expired, '/');
    });

    it('ignores a 401 that is an answer (login, refresh, logout)', () => {
      fail(401, selfHandledContext());
      expect(expireSession).not.toHaveBeenCalled();
    });

    it('ignores a 401 without a session', () => {
      authenticated.set(false);
      fail(401);
      expect(expireSession).not.toHaveBeenCalled();
    });
  });
});
