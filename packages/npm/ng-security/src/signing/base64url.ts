/**
 * Unpadded base64url (`A-Z a-z 0-9 - _`): the one spelling of every MAC, nonce and digest on the
 * wire. Spelled out by hand rather than delegated to `atob`, because a platform decoder's tolerances
 * (padding, whitespace, a permissive final quantum) are not part of the specification both ends
 * share, and the two ends must refuse the same strings.
 */

import type { CryptoBytes } from '../canonical/canonical.ts';

/** The characters from `first` to `last` inclusive, by code point. */
function characterRange(first: string, last: string): string {
  const start = first.charCodeAt(0);
  return Array.from({ length: last.charCodeAt(0) - start + 1 }, (_, offset) => String.fromCharCode(start + offset)).join('');
}

/** The 64 base64url characters in index order: A-Z, a-z, 0-9, `-`, `_`. */
export const BASE64URL_ALPHABET =
  characterRange('A', 'Z') + characterRange('a', 'z') + characterRange('0', '9') + '-_';

const ALPHABET = BASE64URL_ALPHABET;
const BITS_PER_CHAR = 6;
const BITS_PER_BYTE = 8;
const BYTE_MASK = 0xff;
/** Keeps the accumulator inside 16 bits; at most 13 meaningful bits are ever pending. */
const BYTE_MASK_WIDE = 0xffff;
const PLUS = /\+/g;
const SLASH = /\//g;
const PADDING = /=+$/;

/** Unpadded base64url of raw bytes. */
export function base64UrlEncode(bytes: Uint8Array): string {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(PLUS, '-').replace(SLASH, '_').replace(PADDING, '');
}

/** The number of unpadded base64url characters that encode `byteCount` bytes. */
export function base64UrlLength(byteCount: number): number {
  return Math.ceil((byteCount * BITS_PER_BYTE) / BITS_PER_CHAR);
}

/** Exactly `expectedLength` characters of the unpadded base64url alphabet. */
export function isBase64Url(value: unknown, expectedLength: number): value is string {
  if (typeof value !== 'string' || value.length !== expectedLength) return false;
  for (const char of value) {
    if (!ALPHABET.includes(char)) return false;
  }
  return true;
}

/**
 * Decodes exactly `byteCount` bytes, or returns null.
 *
 * Strict and total: the length must be {@link base64UrlLength} of `byteCount`, every character must
 * be in the alphabet, and the bits of the final character that no byte consumes must be zero.
 * Ignoring them would accept four spellings of every 32-byte MAC.
 */
export function decodeBase64UrlStrict(value: string, byteCount: number): CryptoBytes | null {
  if (!isBase64Url(value, base64UrlLength(byteCount))) return null;

  const bytes = new Uint8Array(byteCount);
  let accumulator = 0;
  let bits = 0;
  let written = 0;
  for (const char of value) {
    accumulator = ((accumulator << BITS_PER_CHAR) | ALPHABET.indexOf(char)) & BYTE_MASK_WIDE;
    bits += BITS_PER_CHAR;
    if (bits >= BITS_PER_BYTE) {
      bits -= BITS_PER_BYTE;
      bytes[written++] = (accumulator >> bits) & BYTE_MASK;
    }
  }
  const slackMask = (1 << bits) - 1;
  if (written !== byteCount || (accumulator & slackMask) !== 0) return null;
  return bytes;
}
