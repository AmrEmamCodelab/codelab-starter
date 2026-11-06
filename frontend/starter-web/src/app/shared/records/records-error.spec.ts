import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorCodes, ResponseSignatureError, ResponseSignatureFailures, apiErrorMessageKey } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { AssignmentsErrorCodes, RECORDS_TRANSLATED_CODES, RecordsErrorCodes, WorkflowErrorCodes, recordsApiErrorFrom } from './records-error';

function problem(status: number, code: string): HttpErrorResponse {
  return new HttpErrorResponse({
    status,
    error: { status, code, title: code },
    headers: undefined,
  });
}

describe('records errors', () => {
  it('translates every workflow, record and queue refusal and the generic codes', () => {
    for (const code of [
      ...Object.values(WorkflowErrorCodes),
      ...Object.values(RecordsErrorCodes),
      ...Object.values(AssignmentsErrorCodes),
      ...Object.values(ApiErrorCodes),
    ]) {
      expect(RECORDS_TRANSLATED_CODES.has(code), code).toBe(true);
    }
  });

  it('parses a problem+json refusal and keys its message by code', () => {
    const error = recordsApiErrorFrom(problem(409, WorkflowErrorCodes.ActionNotAllowed));
    expect(error.status).toBe(409);
    expect(apiErrorMessageKey(error, RECORDS_TRANSLATED_CODES)).toBe(`errors.${WorkflowErrorCodes.ActionNotAllowed}`);
  });

  it('turns an unverifiable answer into FORBIDDEN (403) or INTERNAL_ERROR', () => {
    const forbidden = new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status: 403, url: null });
    const failed = new ResponseSignatureError(ResponseSignatureFailures.mismatch, { status: 500, url: null });
    expect(recordsApiErrorFrom(forbidden)).toEqual({ status: 403, code: ApiErrorCodes.forbidden, fieldErrors: [] });
    expect(recordsApiErrorFrom(failed)).toEqual({ status: 500, code: ApiErrorCodes.internalError, fieldErrors: [] });
  });

  it('rethrows anything that is not an HTTP or signature failure', () => {
    const bug = new TypeError('bug');
    expect(() => recordsApiErrorFrom(bug)).toThrow(bug);
  });
});
