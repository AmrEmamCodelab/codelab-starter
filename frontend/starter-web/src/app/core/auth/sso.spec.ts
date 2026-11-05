import { describe, expect, it } from 'vitest';
import { SignInMode, parseSignInMode } from './sign-in-mode';
import { parseSsoCallbackFragment } from './sso';

describe('parseSsoCallbackFragment', () => {
  it('reads the ticket, keeping a + and = of a base64 value', () => {
    expect(parseSsoCallbackFragment('ticket=ab+c%2Fd==')).toEqual({ ticket: 'ab+c/d==', error: null, returnUrl: null });
  });

  it('reads an encoded return URL with its own query string', () => {
    const fragment = `ticket=t1&returnUrl=${encodeURIComponent('/admin/users?page=2&q=a b')}`;
    expect(parseSsoCallbackFragment(fragment).returnUrl).toBe('/admin/users?page=2&q=a b');
  });

  it('reads an error, and ignores a ticket sent with it', () => {
    expect(parseSsoCallbackFragment('error=IDENTITY_SSO_FAILED&ticket=t1')).toEqual({
      ticket: null,
      error: 'IDENTITY_SSO_FAILED',
      returnUrl: null,
    });
  });

  it.each(['', 'ticket=', 'ticket=%E0%A4%A', 'garbage', '=x'])('finds no ticket in %j', fragment => {
    expect(parseSsoCallbackFragment(fragment).ticket).toBeNull();
  });

  it('keeps the first of repeated parameters', () => {
    expect(parseSsoCallbackFragment('ticket=first&ticket=second').ticket).toBe('first');
  });
});

describe('parseSignInMode', () => {
  it.each([
    ['1', SignInMode.Basic],
    ['2', SignInMode.ActiveDirectory],
    ['3', SignInMode.Sso],
    [null, SignInMode.Basic],
    ['9', SignInMode.Basic],
    ['sso', SignInMode.Basic],
  ])('reads %j as %d', (raw, expected) => {
    expect(parseSignInMode(raw)).toBe(expected);
  });

  it('keeps the contract values', () => {
    expect([SignInMode.Basic, SignInMode.ActiveDirectory, SignInMode.Sso]).toEqual([1, 2, 3]);
  });
});
