import { describe, expect, it } from 'vitest';
import {
  MAC_LENGTH,
  MAX_SIGNATURE_HEADER_LENGTH,
  MIN_SIGNATURE_HEADER_LENGTH,
  NONCE_LENGTH,
  RESPONSE_SIGNATURE_HEADER_LENGTH,
  formatResponseSignatureHeader,
  formatSignatureHeader,
  isTimestamp,
  parseResponseSignatureHeader,
  parseSignatureHeader,
  timestampDigits,
} from './signature-header.ts';

const NONCE = 'n'.repeat(NONCE_LENGTH);
const MAC = 'M'.repeat(MAC_LENGTH);

describe('request signature header grammar', () => {
  it('declares lengths that follow from the field sizes', () => {
    expect(NONCE_LENGTH).toBe(22);
    expect(MAC_LENGTH).toBe(43);
    expect(MIN_SIGNATURE_HEADER_LENGTH).toBe(1 + 1 + 1 + 1 + 22 + 1 + 43);
    expect(MAX_SIGNATURE_HEADER_LENGTH).toBe(1 + 1 + 10 + 1 + 22 + 1 + 43);
    expect(RESPONSE_SIGNATURE_HEADER_LENGTH).toBe(45);
  });

  it('round-trips a formatted header', () => {
    const header = formatSignatureHeader({ timestamp: '987654321', nonce: NONCE, mac: MAC });

    expect(header).toBe(`1.987654321.${NONCE}.${MAC}`);
    expect(parseSignatureHeader(header)).toEqual({
      timestamp: '987654321',
      timestampUnixSeconds: 987_654_321,
      nonce: NONCE,
      mac: MAC,
    });
  });

  it('accepts the shortest and the longest legal timestamps', () => {
    expect(parseSignatureHeader(`1.0.${NONCE}.${MAC}`)?.timestamp).toBe('0');
    expect(parseSignatureHeader(`1.9999999999.${NONCE}.${MAC}`)?.timestampUnixSeconds).toBe(9_999_999_999);
  });

  it.each([
    ['an unknown version', `2.1900000000.${NONCE}.${MAC}`],
    ['a fifth field', `1.19.${NONCE}.${MAC}.x`],
    ['a missing field', `1.987654321.${NONCE}${MAC}`],
    ['a leading zero', `1.0987654321.${NONCE}.${MAC}`],
    ['a newline inside the timestamp', `1.190000000\n.${NONCE}.${MAC}`],
    ['Arabic-Indic digits', `1.١٩٠٠٠٠٠٠٠٠.${NONCE}.${MAC}`],
    ['a sign', `1.+190000000.${NONCE}.${MAC}`],
    ['a short nonce', `1.987654321.${NONCE.slice(1)}.${MAC}x`],
    ['a nonce outside the alphabet', `1.987654321.${'+'.repeat(NONCE_LENGTH)}.${MAC}`],
    ['a padded mac', `1.987654321.${NONCE}.${MAC.slice(1)}=`],
    ['a trailing space', `1.190000000.${NONCE}.${MAC} `],
  ])('refuses %s', (_label, header) => {
    expect(parseSignatureHeader(header)).toBeNull();
  });

  it('is total over hostile input and never scans past the maximum length', () => {
    for (const value of [null, undefined, '', '\u0000'.repeat(70), '\ud800'.repeat(70), 'x'.repeat(20_000)]) {
      expect(parseSignatureHeader(value)).toBeNull();
    }
    expect(parseSignatureHeader(`1.987654321.${NONCE}.${MAC}${'.'.repeat(5)}`)).toBeNull();
  });

  it('formats nothing it would not parse', () => {
    expect(formatSignatureHeader({ timestamp: '01', nonce: NONCE, mac: MAC })).toBeNull();
    expect(formatSignatureHeader({ timestamp: '1', nonce: 'short', mac: MAC })).toBeNull();
    expect(formatSignatureHeader({ timestamp: '1', nonce: NONCE, mac: 'short' })).toBeNull();
  });
});

describe('timestampDigits', () => {
  it('turns whole seconds into the digits that are signed and sent', () => {
    expect(timestampDigits(0)).toBe('0');
    expect(timestampDigits(987_654_321)).toBe('987654321');
  });

  it('refuses fractions, negatives, NaN and eleven-digit values', () => {
    for (const value of [1.5, -1, Number.NaN, Number.POSITIVE_INFINITY, 10_000_000_000]) {
      expect(timestampDigits(value)).toBeNull();
    }
  });
});

describe('isTimestamp', () => {
  it('accepts ASCII digits without a leading zero', () => {
    expect(isTimestamp('7')).toBe(true);
    expect(isTimestamp('0')).toBe(true);
  });

  it('refuses everything else', () => {
    for (const value of ['', '00', '12345678901', '１２', '1e9', ' 1', 12]) expect(isTimestamp(value)).toBe(false);
  });
});

describe('response signature header grammar', () => {
  it('round-trips', () => {
    expect(parseResponseSignatureHeader(formatResponseSignatureHeader(MAC))).toBe(MAC);
  });

  it('refuses another version, a wrong length and a bad alphabet', () => {
    for (const value of [`2.${MAC}`, `1.${MAC}x`, `1-${MAC}`, `1.${'+'.repeat(MAC_LENGTH)}`, null, undefined, '']) {
      expect(parseResponseSignatureHeader(value)).toBeNull();
    }
  });

  it('throws when asked to format a malformed MAC (a programming fault)', () => {
    expect(() => formatResponseSignatureHeader('short')).toThrow(TypeError);
  });
});
