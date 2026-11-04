import { describe, expect, it } from 'vitest';
import { BASE64URL_ALPHABET, base64UrlEncode, base64UrlLength, decodeBase64UrlStrict, isBase64Url } from './base64url.ts';

describe('base64UrlEncode', () => {
  it('matches the platform base64url encoder without padding', () => {
    for (const length of [0, 1, 2, 3, 16, 31, 32, 33]) {
      const bytes = crypto.getRandomValues(new Uint8Array(length));
      expect(base64UrlEncode(bytes)).toBe(Buffer.from(bytes).toString('base64url'));
    }
  });

  it('uses - and _ instead of + and /', () => {
    expect(base64UrlEncode(new Uint8Array([0xfb, 0xff]))).toBe('-_8');
  });
});

describe('base64UrlLength', () => {
  it('gives the unpadded length', () => {
    expect(base64UrlLength(16)).toBe(22);
    expect(base64UrlLength(32)).toBe(43);
    expect(base64UrlLength(36)).toBe(48);
  });
});

describe('isBase64Url', () => {
  it('accepts exactly the alphabet at exactly the length', () => {
    expect(isBase64Url('Ab0-_', 5)).toBe(true);
    expect(isBase64Url('Ab0-_', 6)).toBe(false);
  });

  it('refuses padding, standard base64 characters, whitespace and non-strings', () => {
    for (const value of ['Ab0+', 'Ab0/', 'Ab0=', 'Ab0 ', 'Ab0\n', 'Ab0é']) expect(isBase64Url(value, 4)).toBe(false);
    expect(isBase64Url(null, 0)).toBe(false);
    expect(isBase64Url(1234, 4)).toBe(false);
  });
});

describe('decodeBase64UrlStrict', () => {
  it('round-trips random bytes', () => {
    for (const length of [1, 2, 3, 16, 32, 36]) {
      const bytes = crypto.getRandomValues(new Uint8Array(length));
      expect(Array.from(decodeBase64UrlStrict(base64UrlEncode(bytes), length) ?? [])).toEqual(Array.from(bytes));
    }
  });

  it('refuses a non-canonical spelling whose slack bits are not zero', () => {
    const mac = base64UrlEncode(crypto.getRandomValues(new Uint8Array(32)));
    const last = BASE64URL_ALPHABET.indexOf(mac[mac.length - 1]);
    for (const slack of [1, 2, 3]) {
      const variant = mac.slice(0, -1) + BASE64URL_ALPHABET[(last & ~0x03) | slack];
      expect(decodeBase64UrlStrict(variant, 32)).toBeNull();
    }
  });

  it('refuses the wrong length and characters outside the alphabet', () => {
    expect(decodeBase64UrlStrict('AAAA', 32)).toBeNull();
    expect(decodeBase64UrlStrict('A'.repeat(42) + '=', 32)).toBeNull();
    expect(decodeBase64UrlStrict('A'.repeat(42) + '+', 32)).toBeNull();
  });
});

describe('BASE64URL_ALPHABET', () => {
  it('is the 64 RFC 4648 url-safe characters in index order', () => {
    const bytes = Uint8Array.from({ length: 48 }, (_, index) => {
      // Packs the indices 0..63 as consecutive 6-bit groups.
      const bit = index * 8;
      let value = 0;
      for (let offset = 0; offset < 8; offset++) {
        const position = bit + offset;
        value = (value << 1) | ((Math.floor(position / 6) >> (5 - (position % 6))) & 1);
      }
      return value;
    });
    expect(Buffer.from(bytes).toString('base64url')).toBe(BASE64URL_ALPHABET);
    expect(new Set(BASE64URL_ALPHABET).size).toBe(64);
  });
});
