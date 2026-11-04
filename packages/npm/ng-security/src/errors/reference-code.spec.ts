import { describe, expect, it } from 'vitest';
import { isClientTraceId, referenceCodeFrom } from './reference-code.ts';

// Trace ids are assembled, so no 32-hex literal from another system's fixtures is reused.
const LOWER_TRACE = 'c0ffee1234' + '5'.repeat(22);
const UPPER_TRACE = 'FEDCBA9876' + 'A'.repeat(22);

describe('referenceCodeFrom', () => {
  it('takes the first ten hex characters, upper-cased, grouped five and five', () => {
    expect(referenceCodeFrom(LOWER_TRACE)).toBe('C0FFE-E1234');
    expect(referenceCodeFrom(UPPER_TRACE)).toBe('FEDCB-A9876');
  });

  it('returns any other id unchanged rather than truncating it into a code that matches nothing', () => {
    for (const id of ['', 'abc', LOWER_TRACE.slice(0, 16), `${LOWER_TRACE}1`, 'g'.repeat(32), '０'.repeat(32)]) {
      expect(referenceCodeFrom(id)).toBe(id);
    }
  });
});

describe('isClientTraceId', () => {
  it('accepts 32 lower-case hex characters that are not all zero', () => {
    expect(isClientTraceId(LOWER_TRACE)).toBe(true);
  });

  it('refuses everything else', () => {
    for (const id of ['0'.repeat(32), LOWER_TRACE.toUpperCase(), 'abc', '', undefined, null]) {
      expect(isClientTraceId(id)).toBe(false);
    }
  });
});
