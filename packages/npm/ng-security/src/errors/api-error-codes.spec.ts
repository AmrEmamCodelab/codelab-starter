import { describe, expect, it } from 'vitest';
import { ApiErrorCodes, HttpStatus, codeForStatus, isUnlabelledOutage } from './api-error-codes.ts';

describe('ApiErrorCodes', () => {
  it('are unique SCREAMING_CASE values', () => {
    const values = Object.values(ApiErrorCodes);
    expect(new Set(values).size).toBe(values.length);
    for (const value of values) expect(value).toMatch(/^[A-Z][A-Z_]*$/);
  });
});

describe('codeForStatus', () => {
  it('maps every named status and buckets the rest by class', () => {
    expect(codeForStatus(HttpStatus.noResponse)).toBe(ApiErrorCodes.offline);
    expect(codeForStatus(HttpStatus.conflict)).toBe(ApiErrorCodes.concurrency);
    expect(codeForStatus(429)).toBe(ApiErrorCodes.malformed);
    expect(codeForStatus(599)).toBe(ApiErrorCodes.internalError);
  });
});

describe('isUnlabelledOutage', () => {
  it('is true for no response and for 5xx only', () => {
    expect(isUnlabelledOutage(0)).toBe(true);
    expect(isUnlabelledOutage(500)).toBe(true);
    expect(isUnlabelledOutage(404)).toBe(false);
    expect(isUnlabelledOutage(200)).toBe(false);
  });
});
