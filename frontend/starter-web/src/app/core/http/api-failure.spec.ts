import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { parseApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { CORRELATION_ID_HEADER, parseHttpFailure, referenceCodeOf } from './api-failure';

const TRACE_ID = 'd1e8a7c45b2f4e6a9c3b0f7d5a2e8c61';
const TRACE_REFERENCE = 'D1E8A-7C45B';
const PROBLEM = { code: 'BUSINESS_RULE_VIOLATED', correlationId: '7c2e9b4f1a6d3e8c5b0a9f2e4d7c1b36', referenceCode: '7C2E9-B4F1A' };

function failure(status: number, body: unknown, headerId?: string): HttpErrorResponse {
  const headers = headerId === undefined ? new HttpHeaders() : new HttpHeaders({ [CORRELATION_ID_HEADER]: headerId });
  return new HttpErrorResponse({ status, error: body, headers });
}

describe('parseHttpFailure', () => {
  it('reads the reference code from the problem+json body', () => {
    const parsed = parseHttpFailure(failure(409, PROBLEM, TRACE_ID));
    expect(parsed.code).toBe('BUSINESS_RULE_VIOLATED');
    expect(parsed.referenceCode).toBe('7C2E9-B4F1A');
    expect(parsed.correlationId).toBe(PROBLEM.correlationId);
  });

  it('derives the reference code from the body correlation id when the server sent no code', () => {
    const parsed = parseHttpFailure(failure(500, { code: 'INTERNAL_ERROR', correlationId: TRACE_ID }));
    expect(parsed.referenceCode).toBe(TRACE_REFERENCE);
  });

  it('falls back to the correlation header when the body has no id', () => {
    const parsed = parseHttpFailure(failure(413, null, TRACE_ID));
    expect(parsed.correlationId).toBe(TRACE_ID);
    expect(parsed.referenceCode).toBe(TRACE_REFERENCE);
    expect(parsed.status).toBe(413);
  });

  it('reads the header case-insensitively and normalises an upper-case id', () => {
    const headers = new HttpHeaders({ 'x-correlation-id': TRACE_ID.toUpperCase() });
    const parsed = parseHttpFailure(new HttpErrorResponse({ status: 502, error: '<html></html>', headers }));
    expect(parsed.correlationId).toBe(TRACE_ID);
    expect(parsed.referenceCode).toBe(TRACE_REFERENCE);
  });

  it.each(['not-a-trace-id', '0'.repeat(32), '', 'd1e8a7c45b2f4e6a9c3b0f7d5a2e8c'])(
    'ignores a malformed header (%s)',
    (headerId) => {
      const parsed = parseHttpFailure(failure(404, null, headerId));
      expect(parsed.referenceCode).toBeUndefined();
      expect(parsed.correlationId).toBeUndefined();
    },
  );

  it('equals the plain parser when there is neither a body id nor a header', () => {
    const response = failure(404, null);
    expect(parseHttpFailure(response)).toEqual(parseApiError(response));
  });

  it('has no reference code for an offline failure', () => {
    expect(parseHttpFailure(failure(0, null)).referenceCode).toBeUndefined();
  });
});

describe('referenceCodeOf', () => {
  it('reads an HttpErrorResponse (body, then header)', () => {
    expect(referenceCodeOf(failure(409, PROBLEM))).toBe('7C2E9-B4F1A');
    expect(referenceCodeOf(failure(404, null, TRACE_ID))).toBe(TRACE_REFERENCE);
  });

  it('reads an already parsed ApiError', () => {
    expect(referenceCodeOf(parseApiError({ status: 409, error: PROBLEM }))).toBe('7C2E9-B4F1A');
  });

  it('is null when there is no code', () => {
    expect(referenceCodeOf(failure(404, null))).toBeNull();
    expect(referenceCodeOf(parseApiError({ status: 0 }))).toBeNull();
  });

  it.each([new Error('boom'), null, undefined, 'failed', { status: 500 }])('is null for a non-API failure (%s)', (value) => {
    expect(referenceCodeOf(value)).toBeNull();
  });
});
