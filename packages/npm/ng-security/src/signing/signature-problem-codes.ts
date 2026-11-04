/**
 * The `code` values the server's request-signature verifier writes into its 401 problem+json body.
 * Mirrors Codelab.Security's `ProblemCodeFor`. An auth interceptor must treat these as "the signing
 * layer refused this request", never as "the session expired".
 */
export const SignatureProblemCodes = Object.freeze({
  invalid: 'signature_invalid',
  missing: 'signature_missing',
  stale: 'signature_stale',
  keyUnbound: 'signature_key_unbound',
  replayed: 'signature_replayed',
} as const);

export type SignatureProblemCode = (typeof SignatureProblemCodes)[keyof typeof SignatureProblemCodes];

const CODES: ReadonlySet<string> = new Set(Object.values(SignatureProblemCodes));

/** True when `code` is one of the server's signature rejection codes. */
export function isSignatureProblemCode(code: unknown): code is SignatureProblemCode {
  return typeof code === 'string' && CODES.has(code);
}
