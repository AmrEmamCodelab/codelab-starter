import { HttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { provideStarterHttp } from './http.providers';
import { quietContext, selfHandledContext, SKIP_ERROR_DIALOG, SKIP_SESSION_EXPIRY } from './http-context';

describe('provideStarterHttp', () => {
  it('provides HttpClient', () => {
    TestBed.configureTestingModule({ providers: [provideStarterHttp()] });
    expect(TestBed.inject(HttpClient)).toBeInstanceOf(HttpClient);
  });
});

describe('http contexts', () => {
  it('self-handled skips the dialog and the session expiry', () => {
    const context = selfHandledContext();
    expect(context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(context.get(SKIP_SESSION_EXPIRY)).toBe(true);
  });

  it('quiet skips only the dialog', () => {
    const context = quietContext();
    expect(context.get(SKIP_ERROR_DIALOG)).toBe(true);
    expect(context.get(SKIP_SESSION_EXPIRY)).toBe(false);
  });
});
