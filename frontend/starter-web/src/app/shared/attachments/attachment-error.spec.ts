import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { AttachmentErrorCodes, isAttachmentErrorCode } from './attachment-error-codes';
import { AttachmentError, attachmentErrorFrom, clientAttachmentError, rethrowAsAttachmentError } from './attachment-error';

function http(status: number, error: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error });
}

describe('attachmentErrorFrom', () => {
  it('reads code, reference code and status from a problem+json object', async () => {
    const error = await attachmentErrorFrom(
      http(415, { code: AttachmentErrorCodes.SignatureMismatch, correlationId: 'abc', referenceCode: 'ATT01-REF02' }),
    );
    expect(error).toEqual(new AttachmentError(AttachmentErrorCodes.SignatureMismatch, 'ATT01-REF02', 415));
  });

  it('decodes a Blob and a text body', async () => {
    const body = JSON.stringify({ code: AttachmentErrorCodes.FileNotFound, correlationId: 'abc', referenceCode: 'ATT01-REF03' });
    expect(await attachmentErrorFrom(http(404, new Blob([body])))).toMatchObject({ code: AttachmentErrorCodes.FileNotFound });
    expect(await attachmentErrorFrom(http(404, body))).toMatchObject({ referenceCode: 'ATT01-REF03' });
  });

  it('derives the code from the status when the body is not the envelope', async () => {
    expect(await attachmentErrorFrom(http(503, '<html>down</html>'))).toMatchObject({ code: 'RETRYABLE', status: 503, referenceCode: null });
    expect(await attachmentErrorFrom(http(0, null))).toMatchObject({ code: 'NETWORK', status: 0 });
  });

  it('keeps an AttachmentError and maps any other failure to INTERNAL_ERROR', async () => {
    const known = new AttachmentError(AttachmentErrorCodes.EmptyFile, null, 400);
    expect(await attachmentErrorFrom(known)).toBe(known);
    expect(await attachmentErrorFrom(new TypeError('boom'))).toEqual(clientAttachmentError());
  });

  it('rethrows through an observable', async () => {
    await expect(firstValueFrom(rethrowAsAttachmentError(http(409, { code: AttachmentErrorCodes.MaxFilesReached })))).rejects.toMatchObject({
      code: AttachmentErrorCodes.MaxFilesReached,
      status: 409,
    });
  });
});

describe('AttachmentErrorCodes', () => {
  it('are unique and recognised, and platform codes are not', () => {
    const codes = Object.values(AttachmentErrorCodes);
    expect(new Set(codes).size).toBe(codes.length);
    expect(codes.every(isAttachmentErrorCode)).toBe(true);
    expect(isAttachmentErrorCode('INTERNAL_ERROR')).toBe(false);
  });
});
