// Canonical digests for the known-secret denylist. A key can be re-encoded without changing it (hex in upper or lower
// case, base64 or base64url, with or without padding, hex of the base64 bytes). Each value is therefore hashed both
// as written and as the bytes it decodes to, and the scanner checks a candidate the same way, so any of those
// spellings of a denylisted key is found.
import { sha256Hex } from './hashing.mjs';

const MIN_HEX_KEY_CHARS = 32;
const MIN_BASE64_KEY_CHARS = 22;
const HEX = /^[0-9a-fA-F]+$/;
const BASE64 = /^[A-Za-z0-9+/]+={0,2}$/;
const BASE64URL = /^[A-Za-z0-9_-]+$/;
const PADDING = /=+$/;
const HEX_DIGITS_PER_BYTE = 2;

function decodeHex(value) {
  if (value.length < MIN_HEX_KEY_CHARS || value.length % HEX_DIGITS_PER_BYTE !== 0 || !HEX.test(value)) return null;
  return Buffer.from(value, 'hex');
}

function decodeBase64(value) {
  if (value.length < MIN_BASE64_KEY_CHARS) return null;
  const isBase64 = BASE64.test(value);
  const isBase64Url = BASE64URL.test(value);
  if (!isBase64 && !isBase64Url) return null;
  const bytes = Buffer.from(value, isBase64 ? 'base64' : 'base64url');
  const roundTrip = bytes.toString(isBase64 ? 'base64' : 'base64url').replace(PADDING, '');
  return roundTrip === value.replace(PADDING, '') ? bytes : null;
}

/** Every byte sequence the value may encode (hex and base64 readings), possibly none. */
export function decodedKeyBytes(value) {
  return [decodeHex(value), decodeBase64(value)].filter(Boolean);
}

/** The digests that identify a value: its text, and each byte sequence it decodes to. */
export function digestsOf(value) {
  return [sha256Hex(value), ...decodedKeyBytes(value).map((bytes) => sha256Hex(bytes))];
}
