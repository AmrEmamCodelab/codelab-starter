import { describe, expect, it } from 'vitest';
import { ResponseSignatureFailures } from '../verification/response-signature.ts';
import { SignatureProblemCodes, isSignatureProblemCode } from './signature-problem-codes.ts';

describe('SignatureProblemCodes', () => {
  it('names the five server rejection codes', () => {
    expect(Object.values(SignatureProblemCodes).sort()).toEqual([
      'signature_invalid',
      'signature_key_unbound',
      'signature_missing',
      'signature_replayed',
      'signature_stale',
    ]);
  });

  it('recognises exactly those codes', () => {
    for (const code of Object.values(SignatureProblemCodes)) expect(isSignatureProblemCode(code)).toBe(true);
    for (const code of ['UNAUTHENTICATED', 'signature', '', null, 401]) expect(isSignatureProblemCode(code)).toBe(false);
  });

  it('shares no spelling with the client-side response failures', () => {
    const client = new Set<string>(Object.values(ResponseSignatureFailures));
    for (const code of Object.values(SignatureProblemCodes)) expect(client.has(code)).toBe(false);
  });
});
