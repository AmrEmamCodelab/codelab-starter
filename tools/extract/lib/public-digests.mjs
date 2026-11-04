// Digests of public inputs are not secrets, even when a source-product test fixture happened to contain one and the
// harvest denylist therefore lists it. Every signed-body vector with an empty body or an empty JSON object carries
// the SHA-256 of that body, so the digest rule must not report those spellings. The list is of PREIMAGES (public by
// definition), never of values: nothing a scanner could miss is written here.
import { createHash } from 'node:crypto';

/** Public preimages whose SHA-256 is everywhere: the empty body and the empty JSON object. */
export const PUBLIC_PREIMAGES = Object.freeze(['', '{}']);

const BASE64_PADDING = /=+$/;

function spellings(digest) {
  const base64 = digest.toString('base64');
  return [digest.toString('hex'), digest.toString('hex').toUpperCase(), base64, base64.replace(BASE64_PADDING, ''), digest.toString('base64url')];
}

/** Every spelling (hex, base64, base64url) of the SHA-256 of each public preimage. */
export function publicDigestSpellings(preimages = PUBLIC_PREIMAGES) {
  return new Set(preimages.flatMap((preimage) => spellings(createHash('sha256').update(preimage, 'utf8').digest())));
}
