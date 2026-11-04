/**
 * `{Prefix}-CANON-v1`: the TypeScript twin of Codelab.Security's `CanonicalString`.
 *
 * The two must agree byte for byte. They are pinned to each other by the golden vectors in
 * `packages/vectors`, not by shared code.
 *
 * Format: every element (version tag, profile, field count, each field) is emitted as
 * `{utf8ByteLength}:{value}\n`. The length prefixes are the point: unframed concatenation makes
 * `["a","bc"]` and `["ab","c"]` hash identically, so field boundaries become forgeable.
 *
 * Do not "improve" this file. No sorting, no trimming, no case folding, no URL rewriting: each of
 * those would break agreement with the server, and the symptom is an intermittent 401, not a test
 * failure.
 */

import type { SecurityProtocol } from '../protocol/security-protocol.ts';

/** The closed method allowlist. The literal from this list is what enters the canonical form. */
export const ALLOWED_METHODS = Object.freeze([
  'GET',
  'HEAD',
  'POST',
  'PUT',
  'PATCH',
  'DELETE',
  'OPTIONS',
] as const);

export type AllowedMethod = (typeof ALLOWED_METHODS)[number];

/**
 * Bytes WebCrypto accepts. `Uint8Array` is generic over its buffer since TypeScript 5.7 and
 * `BufferSource` needs a plain `ArrayBuffer`; every producer here allocates one, so this narrows the
 * declared type to what is already true instead of casting at each `crypto.subtle` call.
 */
export type CryptoBytes = Uint8Array<ArrayBuffer>;

const encoder = new TextEncoder();

const LENGTH_SEPARATOR = ':';
const ELEMENT_TERMINATOR = '\n';
const PERCENT = '%';
const HEX_RADIX = 16;
const ESCAPE_LENGTH = 3;
const ASCII_LOWER_A = 0x61;
const ASCII_LOWER_Z = 0x7a;
const ASCII_CASE_OFFSET = 0x20;
const QUERY_MARK = '?';
const UNRESERVED = /^[A-Za-z0-9\-._~]$/;
const HEX_DIGIT = /^[0-9a-fA-F]$/;
const LEADING_ASCII_WHITESPACE = /^[ \t\r\n]+/;
const TRAILING_ASCII_WHITESPACE = /[ \t\r\n]+$/;
const ASCII_WHITESPACE_RUN = /[ \t\r\n]+/g;
const SINGLE_SPACE = ' ';

/** The base the query normaliser parses against: it only selects the http(s) query encoding. */
const QUERY_PARSE_BASE = 'http://query.invalid/?';

/** UTF-8 byte length, NOT `String.length` (UTF-16 units), which differs for Arabic. */
function utf8Length(value: string): number {
  return encoder.encode(value).length;
}

function segment(value: string): string {
  return utf8Length(value) + LENGTH_SEPARATOR + value + ELEMENT_TERMINATOR;
}

/**
 * Frames `fields` under `profile`. The profile keeps two constructions over the same values apart.
 *
 * @throws TypeError on a non-string profile or field. A missing value is a programming error:
 * coercing it would let two different call sites produce identical bytes, and `segment(undefined)`
 * would put a length prefix in front of bytes it does not describe.
 */
export function buildCanonical(
  protocol: SecurityProtocol,
  profile: string,
  fields: readonly string[],
): string {
  if (typeof profile !== 'string') {
    throw new TypeError(`${protocol.canonVersion} profile must be a string.`);
  }
  let out = segment(protocol.canonVersion) + segment(profile) + segment(String(fields.length));
  for (const field of fields) {
    if (typeof field !== 'string') {
      throw new TypeError(`${protocol.canonVersion} fields must be strings.`);
    }
    out += segment(field);
  }
  return out;
}

/** The UTF-8 bytes of {@link buildCanonical}: what is actually hashed or signed. */
export function buildCanonicalBytes(
  protocol: SecurityProtocol,
  profile: string,
  fields: readonly string[],
): CryptoBytes {
  return encoder.encode(buildCanonical(protocol, profile, fields));
}

/**
 * Maps a request method onto the allowlist literal, or throws.
 *
 * Upper-cases a-z only to LOOK UP the literal: `toUpperCase()` folds characters (long s, dotless i)
 * that the server's ordinal comparison never would, and the browser would sign a request the server
 * rejects on the method.
 *
 * @throws RangeError when the method is not in {@link ALLOWED_METHODS}.
 */
export function canonicalMethod(method: string): AllowedMethod {
  const upper = upperAscii(method);
  const found = ALLOWED_METHODS.find((candidate) => candidate === upper);
  if (found === undefined) {
    throw new RangeError(`Method '${method}' is not in the canonical method allowlist.`);
  }
  return found;
}

/**
 * Normalises a percent-encoded path for signing: escapes naming an RFC 3986 unreserved character are
 * decoded, every other escape is upper-cased. Both ends run this, so the server's and the browser's
 * different spellings of one path agree by construction. Reserved and non-ASCII escapes stay encoded:
 * decoding `%2F` would turn one segment into two.
 */
export function normalizePathForSigning(path: string): string {
  let out = '';
  let index = 0;
  while (index < path.length) {
    const escape = readEscape(path, index);
    if (escape === null) {
      out += path[index];
      index += 1;
      continue;
    }
    out += escape;
    index += ESCAPE_LENGTH;
  }
  return out;
}

/** The normalised form of the `%XX` escape at `index`, or null when there is none there. */
function readEscape(path: string, index: number): string | null {
  if (path[index] !== PERCENT || index + ESCAPE_LENGTH - 1 >= path.length) return null;
  const high = path[index + 1];
  const low = path[index + ESCAPE_LENGTH - 1];
  if (!HEX_DIGIT.test(high) || !HEX_DIGIT.test(low)) return null;
  const char = String.fromCharCode(parseInt(high + low, HEX_RADIX));
  // Upper-case hex is canonical (RFC 3986, section 6.2.2), so %2f and %2F cannot sign differently.
  return UNRESERVED.test(char) ? char : PERCENT + high.toUpperCase() + low.toUpperCase();
}

/**
 * The blind-index normalisation: trim ASCII whitespace, collapse internal runs to one space, fold
 * a-z only. `trim()` strips Unicode spaces the server keeps, and `toUpperCase()` applies the full
 * Unicode mapping where .NET applies the simple one (`ß` becomes `SS`), so neither is used.
 */
export function normalise(value: string): string {
  return upperAscii(
    value
      .replace(LEADING_ASCII_WHITESPACE, '')
      .replace(TRAILING_ASCII_WHITESPACE, '')
      .replace(ASCII_WHITESPACE_RUN, SINGLE_SPACE),
  );
}

/** a-z only: the one case mapping both runtimes implement identically. */
function upperAscii(value: string): string {
  let out = '';
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    const isLower = code >= ASCII_LOWER_A && code <= ASCII_LOWER_Z;
    out += isLower ? String.fromCharCode(code - ASCII_CASE_OFFSET) : value[index];
  }
  return out;
}

/**
 * Everything after the FIRST `?` of a request URL, in the form the browser sends it, or `''`.
 *
 * A second `?` stays (it is legal in a query). The tail goes through the WHATWG URL parser, because
 * the server signs the query it RECEIVED and the browser escapes a few characters (the apostrophe)
 * on the way out that Angular's parameter encoding leaves alone. Escapes already present pass
 * through untouched; nothing is sorted, decoded or case-folded.
 */
export function queryOf(urlWithParams: string): string {
  const at = urlWithParams.indexOf(QUERY_MARK);
  if (at < 0) return '';
  const tail = urlWithParams.slice(at + 1);
  if (tail === '') return '';
  return new URL(QUERY_PARSE_BASE + tail).search.slice(1);
}
