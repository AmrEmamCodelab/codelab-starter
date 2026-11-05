import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { RoutePaths, SessionEndReasons } from './route-paths';
import { loginTree, safeReturnUrl, unauthorizedTree } from './route-trees';

describe('route trees', () => {
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    router = TestBed.inject(Router);
  });

  it('builds the login URL with the return URL and reason', () => {
    expect(String(loginTree(router))).toBe('/login');
    expect(String(loginTree(router, '/admin'))).toBe('/login?returnUrl=%2Fadmin');
    expect(String(loginTree(router, '/admin', SessionEndReasons.Expired))).toBe(
      '/login?returnUrl=%2Fadmin&reason=expired');
  });

  it('never returns to the login page itself', () => {
    expect(String(loginTree(router, '/login?returnUrl=%2Fx'))).toBe('/login');
  });

  it('builds the 403 URL', () => {
    expect(String(unauthorizedTree(router))).toBe(`/${RoutePaths.Unauthorized}`);
  });

  it.each([
    ['/admin', '/admin'],
    ['/admin?x=1', '/admin?x=1'],
    ['//evil.test', null],
    ['https://evil.test', null],
    ['admin', null],
    ['/login', null],
    ['', null],
    [null, null],
  ])('accepts only app-relative return URLs: %s', (candidate, expected) => {
    expect(safeReturnUrl(candidate)).toBe(expected);
  });
});
