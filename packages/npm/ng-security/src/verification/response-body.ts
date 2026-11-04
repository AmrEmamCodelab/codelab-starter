/**
 * Turns VERIFIED bytes back into the value Angular would have delivered for the requested response
 * type. Delivery, not verification: it runs only after the MAC is checked, and nothing it produces
 * is ever hashed.
 *
 * Text is decoded as UTF-8 regardless of the declared charset. Every Codelab API answers UTF-8 JSON;
 * a service answering in another charset needs this revisited rather than silently mis-decoded.
 */

/** The response types an `HttpRequest` can ask for. */
export type ResponseType = 'arraybuffer' | 'blob' | 'json' | 'text';

/** The decoded body. */
export interface DeliveredBody {
  /** What to hand to the caller. */
  readonly value: unknown;
  /** The undecoded text, kept so a JSON failure can report what it was given. */
  readonly text: string | null;
  /** The `JSON.parse` failure, or null. Non-null only for `json`. */
  readonly jsonParseError: unknown;
}

/** Angular strips this prefix from JSON responses; mirrored so taking over the decode changes nothing. */
const XSSI_PREFIX = /^\)\]\}',?\n/;
const UTF8 = 'utf-8';

/** The bytes in the shape `requested` asks for. */
export function decodeForDelivery(
  bytes: ArrayBuffer | null,
  requested: ResponseType,
  contentType: string,
): DeliveredBody {
  switch (requested) {
    case 'arraybuffer':
      return { value: bytes, text: null, jsonParseError: null };
    case 'blob':
      return {
        value: new Blob(bytes === null ? [] : [bytes], contentType === '' ? {} : { type: contentType }),
        text: null,
        jsonParseError: null,
      };
    case 'text':
      return { value: decodeText(bytes), text: null, jsonParseError: null };
    case 'json':
      return decodeJson(bytes);
  }
}

function decodeJson(bytes: ArrayBuffer | null): DeliveredBody {
  const original = decodeText(bytes);
  const stripped = original.replace(XSSI_PREFIX, '');
  if (stripped === '') return { value: null, text: original, jsonParseError: null };
  try {
    return { value: JSON.parse(stripped), text: original, jsonParseError: null };
  } catch (error) {
    return { value: original, text: original, jsonParseError: error };
  }
}

/** UTF-8, replacing malformed sequences exactly as the transport would. */
function decodeText(bytes: ArrayBuffer | null): string {
  return bytes === null ? '' : new TextDecoder(UTF8).decode(bytes);
}
