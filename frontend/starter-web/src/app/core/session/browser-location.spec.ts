import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BrowserLocation, isNavigableUrl, parseFragmentParams } from './browser-location';

interface FakeWindow {
  location: { hash: string; pathname: string; search: string; assign: ReturnType<typeof vi.fn> };
  history: { state: unknown; replaceState: ReturnType<typeof vi.fn> };
}

describe('BrowserLocation', () => {
  let window: FakeWindow;

  beforeEach(() => {
    window = {
      location: { hash: '#ticket=t1', pathname: '/auth/sso/callback', search: '?x=1', assign: vi.fn() },
      history: { state: { navigationId: 3 }, replaceState: vi.fn() },
    };
    TestBed.configureTestingModule({
      providers: [{ provide: DOCUMENT, useValue: { defaultView: window } }],
    });
  });

  it('navigates the whole page to an absolute http(s) URL', () => {
    expect(TestBed.inject(BrowserLocation).assign('https://idp.test/end')).toBe(true);
    expect(window.location.assign).toHaveBeenCalledWith('https://idp.test/end');
  });

  it.each(['javascript:alert(1)', '/relative', 'not a url', 'data:text/html,x'])('refuses to navigate to %s', url => {
    expect(TestBed.inject(BrowserLocation).assign(url)).toBe(false);
    expect(window.location.assign).not.toHaveBeenCalled();
  });

  it('reads the raw fragment without its mark', () => {
    expect(TestBed.inject(BrowserLocation).fragment()).toBe('ticket=t1');
  });

  it('clears the fragment from the history entry, keeping path, query and state', () => {
    TestBed.inject(BrowserLocation).clearFragment();
    expect(window.history.replaceState).toHaveBeenCalledWith({ navigationId: 3 }, '', '/auth/sso/callback?x=1');
  });

  it('leaves the history alone when there is no fragment', () => {
    window.location.hash = '';
    TestBed.inject(BrowserLocation).clearFragment();
    expect(window.history.replaceState).not.toHaveBeenCalled();
  });
});

describe('isNavigableUrl', () => {
  it('accepts http and https only', () => {
    expect(isNavigableUrl('http://localhost:5180/api/auth/sso/login')).toBe(true);
    expect(isNavigableUrl('ftp://host/')).toBe(false);
  });
});

describe('parseFragmentParams', () => {
  it('decodes each value on its own', () => {
    expect([...parseFragmentParams('a=1%2B2&b=x+y')]).toEqual([
      ['a', '1+2'],
      ['b', 'x+y'],
    ]);
  });

  it('drops pairs without a name or with a broken escape', () => {
    expect([...parseFragmentParams('=v&x&y=%E0%A4%A&z=ok')]).toEqual([['z', 'ok']]);
  });
});
