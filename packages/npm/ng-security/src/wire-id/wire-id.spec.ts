import { describe, expect, expectTypeOf, it } from 'vitest';
import {
  NotAWireIdError,
  WIRE_ID_PAYLOAD_LENGTH,
  asId,
  defineIdDomains,
  isWireId,
  toIdSet,
  tryAsId,
  type WireId,
} from './wire-id.ts';

/** Shape-valid tokens built at runtime: shape is all the browser checks. */
function token(keyId = 'k1', fill = 'A'): string {
  return `m1_${keyId}_${fill.repeat(WIRE_ID_PAYLOAD_LENGTH)}`;
}

describe('wire ids: the one narrowing door', () => {
  describe('shape', () => {
    it('accepts a well-formed token', () => {
      expect(isWireId(token())).toBe(true);
      expect(tryAsId('record', token())).toBe(token());
    });

    it('accepts every key id length the format allows, and no other', () => {
      for (let length = 1; length <= 8; length++) expect(isWireId(token('k'.repeat(length)))).toBe(true);
      expect(isWireId(token(''))).toBe(false);
      expect(isWireId(token('k'.repeat(9)))).toBe(false);
      expect(isWireId(token('K1'))).toBe(false);
    });

    it('refuses a decimal id: the plaintext-accepted-on-read defect, closed in the browser too', () => {
      for (const raw of ['0', '43', '-1', '1e3']) expect(tryAsId('record', raw)).toBeNull();
    });

    it('refuses the other shapes an id position might be handed', () => {
      for (const raw of ['', 'REQ-2026-001', '3f2504e0-4f89-11d3-9a0c-0305e82c3301', null, undefined, `${token()} `, `m2_k1_${'A'.repeat(48)}`]) {
        expect(tryAsId('record', raw)).toBeNull();
      }
    });

    it('refuses a payload with characters outside base64url or of the wrong length', () => {
      expect(isWireId(`m1_k1_${'A'.repeat(47)}=`)).toBe(false);
      expect(isWireId(`m1_k1_${'A'.repeat(47)}+`)).toBe(false);
      expect(isWireId(`m1_k1_${'A'.repeat(49)}`)).toBe(false);
      expect(isWireId(42)).toBe(false);
    });
  });

  describe('asId', () => {
    it('returns the branded id or throws without echoing the value', () => {
      expect(asId('record', token())).toBe(token());
      expect(() => asId('record', 'secret-value')).toThrow(NotAWireIdError);
      expect(() => asId('record', 'secret-value')).toThrow(/expected domain 'record'/);
      try {
        asId('record', 'secret-value');
      } catch (error) {
        expect(String(error)).not.toContain('secret-value');
      }
    });
  });

  describe('toIdSet, the replacement for map(Number)', () => {
    it('keeps two distinct ids distinct and de-duplicates a repeat', () => {
      const set = toIdSet('record', [token('k1', 'A'), token('k1', 'B'), token('k1', 'A')]);
      expect(set.size).toBe(2);
    });

    it('throws on a non-token instead of admitting NaN', () => {
      expect(() => toIdSet('record', [token(), 7])).toThrow(NotAWireIdError);
    });

    it('never yields NaN, whatever it is given', () => {
      for (const value of [Number.NaN, 0, 'NaN']) expect(() => toIdSet('record', [value])).toThrow();
    });
  });

  describe('the domain argument: what it proves, and what it does not', () => {
    it('does NOT reject a well-formed token of another family (the server checks that)', () => {
      expect(tryAsId('order', token())).toBe(tryAsId('record', token()));
    });

    it('brands the result so ids of two domains are not assignable to each other', () => {
      const id = asId('record', token());
      expectTypeOf(id).toEqualTypeOf<WireId<'record'>>();
      expectTypeOf(id).not.toEqualTypeOf<WireId<'order'>>();
      expectTypeOf(id).toMatchTypeOf<string>();
    });
  });
});

describe('defineIdDomains', () => {
  it('returns the frozen roster', () => {
    const domains = defineIdDomains(['record', 'user', 'audit-log'] as const);
    expect(domains).toEqual(['record', 'user', 'audit-log']);
    expect(Object.isFrozen(domains)).toBe(true);
  });

  it('refuses duplicates and labels that are not kebab-case', () => {
    expect(() => defineIdDomains(['record', 'record'])).toThrow(RangeError);
    for (const label of ['Record', 'record_id', 'record--id', '-record', '', '1record']) {
      expect(() => defineIdDomains([label])).toThrow(RangeError);
    }
  });
});
