/**
 * The signature header grammars, shared by both ends.
 *
 * Request header `X-{Prefix}-Signature`: `1.{timestamp}.{nonce}.{mac}`.
 * Response header `X-{Prefix}-Response-Signature`: `1.{mac}`.
 *
 * The parsers are hand-written scans, not regular expressions: in .NET `$` matches before a final
 * newline, so a regex grammar would accept `'987654321\n'` on one end and reject it on the other.
 * Both ends compare code points.
 */

import { base64UrlLength, isBase64Url } from './base64url.ts';

/** The only scheme version this build understands. An unknown one is refused, never guessed at. */
export const SIGNATURE_SCHEME_VERSION = '1';

/** Bytes of CSPRNG nonce. */
export const NONCE_BYTES = 16;
/** Bytes of HMAC-SHA256 output. */
export const MAC_BYTES = 32;
/** 16 bytes as unpadded base64url. */
export const NONCE_LENGTH = base64UrlLength(NONCE_BYTES);
/** 32 bytes as unpadded base64url. */
export const MAC_LENGTH = base64UrlLength(MAC_BYTES);
/** Ten digits covers Unix seconds to the year 2286; eleven is not a timestamp. */
export const MAX_TIMESTAMP_DIGITS = 10;
/** The largest timestamp the grammar admits. */
export const MAX_TIMESTAMP_SECONDS = 9_999_999_999;

const SEPARATOR = '.';
const SEPARATOR_COUNT = 3;
const MIN_TIMESTAMP_DIGITS = 1;
const ASCII_ZERO = 0x30;
const ASCII_NINE = 0x39;

/** Shortest legal request header: a one-digit timestamp. */
export const MIN_SIGNATURE_HEADER_LENGTH =
  SIGNATURE_SCHEME_VERSION.length + SEPARATOR_COUNT + MIN_TIMESTAMP_DIGITS + NONCE_LENGTH + MAC_LENGTH;
/** Longest legal request header. Anything longer is refused before a field is examined. */
export const MAX_SIGNATURE_HEADER_LENGTH =
  SIGNATURE_SCHEME_VERSION.length + SEPARATOR_COUNT + MAX_TIMESTAMP_DIGITS + NONCE_LENGTH + MAC_LENGTH;
/** The response header is fixed-length. */
export const RESPONSE_SIGNATURE_HEADER_LENGTH =
  SIGNATURE_SCHEME_VERSION.length + SEPARATOR.length + MAC_LENGTH;

/** A parsed request signature header. */
export interface SignatureHeaderParts {
  /** Exactly as it appeared on the wire: this string, not the number, is signed. */
  readonly timestamp: string;
  /** The timestamp as a number, for the freshness window only. */
  readonly timestampUnixSeconds: number;
  readonly nonce: string;
  readonly mac: string;
}

/** The parts of a request signature header to format. */
export interface SignatureHeaderInput {
  readonly timestamp: string;
  readonly nonce: string;
  readonly mac: string;
}

/**
 * Parses a request signature header, or returns null. Total over any input: null, 20 000 characters,
 * lone surrogates and NUL all return null, and nothing longer than the maximum is scanned.
 */
export function parseSignatureHeader(value: string | null | undefined): SignatureHeaderParts | null {
  if (typeof value !== 'string') return null;
  if (value.length < MIN_SIGNATURE_HEADER_LENGTH || value.length > MAX_SIGNATURE_HEADER_LENGTH) {
    return null;
  }
  const parts = value.split(SEPARATOR);
  // A fourth separator means a fifth field, which must never be silently ignored.
  if (parts.length !== SEPARATOR_COUNT + 1) return null;
  const [version, timestamp, nonce, mac] = parts;
  if (version !== SIGNATURE_SCHEME_VERSION) return null;
  if (!isTimestamp(timestamp)) return null;
  if (!isBase64Url(nonce, NONCE_LENGTH) || !isBase64Url(mac, MAC_LENGTH)) return null;
  return { timestamp, timestampUnixSeconds: Number(timestamp), nonce, mac };
}

/**
 * Builds `1.{timestamp}.{nonce}.{mac}` under the same grammar the parser applies, so anything this
 * emits re-parses. Returns null when the parts do not satisfy it: a caller that gets null must fail
 * closed and send nothing, never an unsigned request.
 */
export function formatSignatureHeader(input: SignatureHeaderInput): string | null {
  if (!isTimestamp(input.timestamp)) return null;
  if (!isBase64Url(input.nonce, NONCE_LENGTH) || !isBase64Url(input.mac, MAC_LENGTH)) return null;
  return [SIGNATURE_SCHEME_VERSION, input.timestamp, input.nonce, input.mac].join(SEPARATOR);
}

/** The timestamp digits for a number of Unix seconds, or null when it is not a legal timestamp. */
export function timestampDigits(unixSeconds: number): string | null {
  if (!Number.isInteger(unixSeconds)) return null;
  if (unixSeconds < 0 || unixSeconds > MAX_TIMESTAMP_SECONDS) return null;
  return String(unixSeconds);
}

/**
 * Parses a response signature header to its MAC, or returns null. Null means the response carries
 * no verifiable signature; an unsigned response is never rendered as data.
 */
export function parseResponseSignatureHeader(value: string | null | undefined): string | null {
  if (typeof value !== 'string' || value.length !== RESPONSE_SIGNATURE_HEADER_LENGTH) return null;
  const lead = SIGNATURE_SCHEME_VERSION + SEPARATOR;
  if (!value.startsWith(lead)) return null;
  const mac = value.slice(lead.length);
  return isBase64Url(mac, MAC_LENGTH) ? mac : null;
}

/**
 * Formats `1.{mac}`. For symmetry with the server and for the golden vectors; the browser never
 * emits this header.
 *
 * @throws TypeError when `mac` is not 43 characters of unpadded base64url (a programming fault).
 */
export function formatResponseSignatureHeader(mac: string): string {
  if (!isBase64Url(mac, MAC_LENGTH)) {
    throw new TypeError(`A response MAC must be ${MAC_LENGTH} characters of unpadded base64url.`);
  }
  return SIGNATURE_SCHEME_VERSION + SEPARATOR + mac;
}

/**
 * ASCII digits only, 1 to 10, no sign, no leading zero unless the field is a lone `0`. Code points,
 * never a "digit" predicate: Arabic-Indic and fullwidth digits must not be timestamps. Leading zeros
 * are refused because two spellings of one instant would be two proofs for the replay store to miss.
 */
export function isTimestamp(value: unknown): value is string {
  if (typeof value !== 'string') return false;
  if (value.length < MIN_TIMESTAMP_DIGITS || value.length > MAX_TIMESTAMP_DIGITS) return false;
  if (value.length > MIN_TIMESTAMP_DIGITS && value.charCodeAt(0) === ASCII_ZERO) return false;
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code < ASCII_ZERO || code > ASCII_NINE) return false;
  }
  return true;
}
