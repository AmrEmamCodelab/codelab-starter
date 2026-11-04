import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { ApiErrorCodes, apiErrorMessageKey, parseApiError, parseFieldErrors } from './api-error.ts';

// Trace ids are assembled, so no 32-hex literal from another system's fixtures is reused.
const TRACE = 'c0ffee1234' + '5'.repeat(22);
const CLIENT_TRACE = '0a1b2c3d4e' + '6'.repeat(22);

function http(status: number, error: unknown) {
  return new HttpErrorResponse({ status, error });
}

describe('parseApiError', () => {
  it('reads the problem+json envelope: code, correlationId, referenceCode and detail', () => {
    const parsed = parseApiError(
      http(409, { title: 'Conflict', status: 409, code: 'CONCURRENCY', correlationId: TRACE, referenceCode: 'SRV01-CODE2', detail: 'Changed.' }),
    );

    expect(parsed).toEqual({
      status: 409,
      code: 'CONCURRENCY',
      serverCode: 'CONCURRENCY',
      correlationId: TRACE,
      referenceCode: 'SRV01-CODE2',
      detail: 'Changed.',
      fieldErrors: [],
    });
  });

  it('derives the reference code from a 32-hex correlationId when the body carries none', () => {
    expect(parseApiError(http(500, { code: 'INTERNAL_ERROR', correlationId: TRACE })).referenceCode).toBe('C0FFE-E1234');
  });

  it('falls back to the raw id when it is not 32 hex, and carries no code without an id', () => {
    expect(parseApiError(http(500, { correlationId: 'legacy-id' })).referenceCode).toBe('legacy-id');
    expect(parseApiError(http(500, { referenceCode: 'ORPHN-CODE0' })).referenceCode).toBeUndefined();
  });

  it('flattens the validation dictionary and strips the binder JSON-path prefix', () => {
    const parsed = parseApiError(
      http(400, { code: 'VALIDATION_FAILED', errors: { '$.address.city': ['Required.'], Name: ['Too long.', 'Bad.'] } }),
    );

    expect(parsed.fieldErrors).toEqual([
      { field: 'address.city', message: 'Required.' },
      { field: 'Name', message: 'Too long.' },
      { field: 'Name', message: 'Bad.' },
    ]);
  });

  it('survives a network failure and quotes the client-minted trace id there', () => {
    expect(parseApiError(http(0, { type: 'error' }))).toEqual({ status: 0, code: ApiErrorCodes.offline, fieldErrors: [] });
    expect(parseApiError(http(0, null), CLIENT_TRACE).referenceCode).toBe('0A1B2-C3D4E');
  });

  it('survives an HTML error page from a proxy', () => {
    expect(parseApiError(http(502, '<html>Bad Gateway</html>'))).toEqual({ status: 502, code: ApiErrorCodes.upstream, fieldErrors: [] });
  });

  describe('a 5xx with no server id (gateway page, offline app, timeout)', () => {
    it.each([502, 503, 504])('quotes the client-minted id for %i', (status) => {
      expect(parseApiError(http(status, ''), CLIENT_TRACE).correlationId).toBe(CLIENT_TRACE);
    });

    it('keeps the server id when the 5xx carries one', () => {
      expect(parseApiError(http(500, { correlationId: TRACE }), CLIENT_TRACE).correlationId).toBe(TRACE);
    });

    it('leaves a 4xx without an id unlabelled, and ignores an unusable client id', () => {
      expect(parseApiError(http(404, {}), CLIENT_TRACE).correlationId).toBeUndefined();
      expect(parseApiError(http(503, ''), 'not-a-trace-id').correlationId).toBeUndefined();
      expect(parseApiError(http(503, ''), '0'.repeat(32)).correlationId).toBeUndefined();
    });
  });

  it('derives the code from a bare status the way the server would have', () => {
    const cases: [number, string][] = [
      [400, 'MALFORMED_REQUEST'], [401, 'UNAUTHENTICATED'], [403, 'FORBIDDEN'], [404, 'NOT_FOUND'],
      [405, 'MALFORMED_REQUEST'], [409, 'CONCURRENCY'], [413, 'INVALID_FILE'], [415, 'MALFORMED_REQUEST'],
      [422, 'BUSINESS_RULE_VIOLATED'], [418, 'MALFORMED_REQUEST'], [500, 'INTERNAL_ERROR'], [502, 'UPSTREAM_UNAVAILABLE'],
      [503, 'RETRYABLE'], [504, 'INTERNAL_ERROR'],
    ];
    for (const [status, code] of cases) expect(parseApiError(http(status, null)).code).toBe(code);
  });

  it('distinguishes a code the server sent from one derived off the status', () => {
    expect(parseApiError(http(409, { code: 'RECORD_LOCKED' })).serverCode).toBe('RECORD_LOCKED');
    expect(parseApiError(http(409, {})).serverCode).toBeUndefined();
    expect(parseApiError(http(409, {})).code).toBe('CONCURRENCY');
  });

  it('accepts any { status, error } value and never throws', () => {
    expect(parseApiError({ status: 404, error: { code: 'NOT_FOUND' } }).code).toBe('NOT_FOUND');
    expect(parseApiError({}).code).toBe(ApiErrorCodes.offline);
    expect(parseApiError(undefined as unknown as { status: number }).status).toBe(0);
  });
});

describe('parseFieldErrors', () => {
  it('reads nothing from bodies without a dictionary', () => {
    for (const body of [null, undefined, 'text', {}, { errors: null }, { errors: [{ field: 'a', message: 'b' }] }]) {
      expect(parseFieldErrors(body)).toEqual([]);
    }
  });

  it('accepts a single message and drops empty ones', () => {
    expect(parseFieldErrors({ errors: { a: 'One.', b: ['', null, 'Two.'] } })).toEqual([
      { field: 'a', message: 'One.' },
      { field: 'b', message: 'Two.' },
    ]);
  });
});

describe('apiErrorMessageKey', () => {
  it('produces errors.<CODE> for a platform code', () => {
    expect(apiErrorMessageKey(parseApiError(http(404, { code: 'NOT_FOUND' })))).toBe('errors.NOT_FOUND');
  });

  it('falls back to a real sentence for a code no bundle knows, instead of the raw key', () => {
    expect(apiErrorMessageKey(parseApiError(http(409, { code: 'RECORD_LOCKED' })))).toBe('errors.INTERNAL_ERROR');
  });

  it('uses the app-supplied translated codes when given', () => {
    const known = new Set<string>([...Object.values(ApiErrorCodes), 'RECORD_LOCKED']);
    expect(apiErrorMessageKey(parseApiError(http(409, { code: 'RECORD_LOCKED' })), known)).toBe('errors.RECORD_LOCKED');
  });
});
