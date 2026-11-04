import { describe, expect, it } from 'vitest';
import { base64Of, randomKeyBytes, signingBlock } from '../testing/test-support.ts';
import {
  MIN_SESSION_KEY_BYTES,
  SigningEnvironmentUnsupportedError,
  SigningMaterialRejectedError,
  SigningRefusalReasons,
  assertSigningBlock,
  decodeSessionKey,
} from './signing-material.ts';

describe('assertSigningBlock', () => {
  it('accepts a well-formed block', () => {
    expect(() => assertSigningBlock(signingBlock(randomKeyBytes()))).not.toThrow();
  });

  it.each([
    [{ alg: 'HMAC-SHA512' }, 'algorithm'],
    [{ audience: '' }, 'audience'],
    [{ audience: undefined as unknown as string }, 'audience'],
    [{ serverTimeUnix: -5 }, 'server time'],
    [{ serverTimeUnix: Number.NaN }, 'server time'],
  ])('refuses %j and names the problem', (overrides, words) => {
    expect(() => assertSigningBlock(signingBlock(randomKeyBytes(), overrides))).toThrow(
      expect.objectContaining({ name: 'SigningMaterialRejectedError', message: expect.stringContaining(words) }),
    );
  });
});

describe('decodeSessionKey', () => {
  it('decodes standard base64 to the raw bytes', () => {
    const bytes = randomKeyBytes();
    expect(Array.from(decodeSessionKey(base64Of(bytes)))).toEqual(Array.from(bytes));
  });

  it('accepts longer keys and refuses shorter ones', () => {
    expect(decodeSessionKey(base64Of(randomKeyBytes(64)))).toHaveLength(64);
    expect(() => decodeSessionKey(base64Of(randomKeyBytes(MIN_SESSION_KEY_BYTES - 1)))).toThrow(SigningMaterialRejectedError);
  });

  it('refuses absent and non-base64 material', () => {
    for (const value of ['', '%%%', undefined as unknown as string]) {
      expect(() => decodeSessionKey(value)).toThrow(SigningMaterialRejectedError);
    }
  });
});

describe('refusal reasons', () => {
  it('are distinct and carried by the errors', () => {
    expect(new Set(Object.values(SigningRefusalReasons)).size).toBe(3);
    expect(new SigningMaterialRejectedError('x').reason).toBe(SigningRefusalReasons.materialRejected);
    expect(new SigningEnvironmentUnsupportedError('x').reason).toBe(SigningRefusalReasons.unsupported);
  });
});
