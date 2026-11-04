import { describe, expect, it } from 'vitest';
import { TEST_PROTOCOL } from '../testing/test-support.ts';
import { createSecurityProtocol } from '../protocol/security-protocol.ts';
import {
  ALLOWED_METHODS,
  buildCanonical,
  buildCanonicalBytes,
  canonicalMethod,
  normalise,
  normalizePathForSigning,
  queryOf,
} from './canonical.ts';

describe('buildCanonical', () => {
  it('frames every element as {utf8 length}:{value}\\n, version and count first', () => {
    expect(buildCanonical(TEST_PROTOCOL, 'P', ['a', 'bc'])).toBe(
      '20:CODELABTEST-CANON-v1\n1:P\n1:2\n1:a\n2:bc\n',
    );
  });

  it('frames an empty field list and empty fields explicitly', () => {
    expect(buildCanonical(TEST_PROTOCOL, 'P', [])).toBe('20:CODELABTEST-CANON-v1\n1:P\n1:0\n');
    expect(buildCanonical(TEST_PROTOCOL, 'P', [''])).toBe('20:CODELABTEST-CANON-v1\n1:P\n1:1\n0:\n');
  });

  it('carries the protocol version, so two prefixes never produce the same bytes', () => {
    const other = createSecurityProtocol('ALTPFX');

    expect(buildCanonical(other, 'P', ['a'])).toBe('15:ALTPFX-CANON-v1\n1:P\n1:1\n1:a\n');
    expect(buildCanonical(other, 'P', ['a'])).not.toBe(buildCanonical(TEST_PROTOCOL, 'P', ['a']));
  });

  it('keeps unframed-colliding field lists apart (the forgeable-boundary defect)', () => {
    const pairs: [string[], string[]][] = [
      [['a', 'bc'], ['ab', 'c']],
      [['', 'a'], ['a', '']],
      [['a'], ['a', '']],
      [['1:a\n'], ['a']],
    ];
    for (const [left, right] of pairs) {
      expect(buildCanonical(TEST_PROTOCOL, 'P', left)).not.toBe(buildCanonical(TEST_PROTOCOL, 'P', right));
    }
  });

  it('separates profiles over the same fields', () => {
    expect(buildCanonical(TEST_PROTOCOL, 'REQ', ['a'])).not.toBe(buildCanonical(TEST_PROTOCOL, 'RESP', ['a']));
  });

  it('does not let a field containing framing syntax forge a boundary', () => {
    expect(buildCanonical(TEST_PROTOCOL, 'P', ['3:abc\n'])).not.toBe(buildCanonical(TEST_PROTOCOL, 'P', ['abc']));
  });

  it('counts UTF-8 bytes, not UTF-16 units, for Arabic', () => {
    const arabic = 'طلب';
    const text = buildCanonical(TEST_PROTOCOL, 'P', [arabic]);
    const bytes = buildCanonicalBytes(TEST_PROTOCOL, 'P', [arabic]);

    expect(text.endsWith(`6:${arabic}\n`)).toBe(true);
    expect(Array.from(bytes)).toEqual(Array.from(new TextEncoder().encode(text)));
    expect(bytes.length).toBeGreaterThan(text.length);
  });

  it('throws on a non-string field rather than coercing it', () => {
    expect(() => buildCanonical(TEST_PROTOCOL, 'P', ['a', null as unknown as string])).toThrow(TypeError);
    expect(() => buildCanonical(TEST_PROTOCOL, 'P', [undefined as unknown as string])).toThrow(TypeError);
    expect(() => buildCanonical(TEST_PROTOCOL, 'P', [7 as unknown as string])).toThrow(TypeError);
  });

  it('throws on a non-string profile rather than mis-framing it', () => {
    for (const profile of [undefined, null, 7]) {
      expect(() => buildCanonical(TEST_PROTOCOL, profile as unknown as string, ['a'])).toThrow(TypeError);
    }
  });
});

describe('canonicalMethod', () => {
  it('returns the allowlist literal for any ASCII spelling', () => {
    for (const method of ALLOWED_METHODS) {
      expect(canonicalMethod(method)).toBe(method);
      expect(canonicalMethod(method.toLowerCase())).toBe(method);
    }
    expect(canonicalMethod('pOsT')).toBe('POST');
  });

  it('refuses methods outside the allowlist, including Unicode case-fold look-alikes', () => {
    for (const method of ['TRACE', 'CONNECT', '', ' GET', 'GET ', 'poſt', 'optıons', 'GEТ']) {
      expect(() => canonicalMethod(method)).toThrow(RangeError);
    }
  });

  it('is frozen', () => {
    expect(Object.isFrozen(ALLOWED_METHODS)).toBe(true);
  });
});

describe('normalizePathForSigning', () => {
  it('decodes escapes of unreserved characters', () => {
    expect(normalizePathForSigning('/items/%41%62%7E%2D%2e%5F')).toBe('/items/Ab~-._');
  });

  it('upper-cases every other escape and never decodes a reserved one', () => {
    expect(normalizePathForSigning('/a%2fb%2Fc')).toBe('/a%2Fb%2Fc');
    expect(normalizePathForSigning('/%d8%b7')).toBe('/%D8%B7');
    expect(normalizePathForSigning('/a%20b')).toBe('/a%20b');
  });

  it('leaves malformed or truncated escapes and plain text untouched', () => {
    expect(normalizePathForSigning('/a%zz/b%4')).toBe('/a%zz/b%4');
    expect(normalizePathForSigning('/a%')).toBe('/a%');
    expect(normalizePathForSigning('/Items/Ünïcode')).toBe('/Items/Ünïcode');
  });

  it('is idempotent', () => {
    const once = normalizePathForSigning('/a%2fb/%7e/%d8%b7');
    expect(normalizePathForSigning(once)).toBe(once);
  });
});

describe('normalise', () => {
  it('trims and collapses ASCII whitespace and folds a-z', () => {
    expect(normalise('  ab \t\r\n cd  ')).toBe('AB CD');
  });

  it('folds a-z only, never a library uppercase', () => {
    expect(normalise('straße')).toBe('STRAßE');
    expect(normalise('ıstanbul')).toBe('ıSTANBUL');
    expect(normalise('istanbul')).toBe('ISTANBUL');
  });

  it('keeps Unicode spaces that the server keeps', () => {
    expect(normalise(' name　')).toBe(' NAME　');
  });
});

describe('queryOf', () => {
  it('returns everything after the first ?', () => {
    expect(queryOf('/items?status=2&page=1')).toBe('status=2&page=1');
    expect(queryOf('http://localhost:5000/items?a=1')).toBe('a=1');
  });

  it('keeps a second ? instead of splitting on it', () => {
    expect(queryOf('/search?q=who?&page=2')).toBe('q=who?&page=2');
    expect(queryOf('/search?a=1?2?3')).toBe('a=1?2?3');
  });

  it('returns an apostrophe in the form the browser sends it', () => {
    expect(queryOf("/items?search=O'Neil&page=1")).toBe('search=O%27Neil&page=1');
  });

  it('leaves a query already in wire form untouched', () => {
    for (const query of [
      'search=%D8%A8%D8%AD%D8%AB&page=1',
      'search=a%20b',
      'search=a+b',
      'q=!*()~-._',
      'statuses=1,2,3&from=2026-09-21',
      'search=O%27Neil',
      'q=%2f%2F',
    ]) {
      expect(queryOf(`/items?${query}`)).toBe(query);
    }
  });

  it('returns empty for no query and for a bare trailing ?', () => {
    expect(queryOf('/items')).toBe('');
    expect(queryOf('')).toBe('');
    expect(queryOf('/items?')).toBe('');
  });
});
