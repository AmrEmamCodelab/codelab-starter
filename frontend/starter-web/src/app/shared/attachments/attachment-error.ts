import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorCodes, HttpStatus, parseApiError } from '@codelab/ng-security';
import { from, throwError, type Observable } from 'rxjs';
import { concatMap } from 'rxjs/operators';

/**
 * Why an attachment call failed, read from the problem+json of the API OR the attachment service (both write the
 * Codelab.Errors envelope). Render `errors.<code>` and quote `referenceCode` when present.
 *
 * - `code`: the server's code (an `AttachmentErrorCodes` value or a platform code); derived from the status when the
 *   body carried none; `INTERNAL_ERROR` for a failure that was not an HTTP response at all.
 * - `referenceCode`: the short code the user reads out to support, or null.
 * - `status`: the HTTP status; 0 when nothing came back (offline, CORS) or the failure happened in the browser.
 */
export class AttachmentError extends Error {
  constructor(
    readonly code: string,
    readonly referenceCode: string | null,
    readonly status: number,
  ) {
    super(code);
    this.name = 'AttachmentError';
  }
}

/**
 * A failure the browser raised itself (a refused ticket header, an untrusted upload URL, an answer without the stored
 * file): `INTERNAL_ERROR` with no reference code, and `status` 0 unless a response did arrive.
 */
export function clientAttachmentError(status: number = HttpStatus.noResponse): AttachmentError {
  return new AttachmentError(ApiErrorCodes.internalError, null, status);
}

/**
 * Reads any failure of the attachment flow into one {@link AttachmentError}. Never rejects.
 *
 * A body that arrived as a `Blob` (a download's error) or as text is decoded as JSON first, so a refused download
 * carries its code like any other call. An `AttachmentError` passes through unchanged.
 */
export async function attachmentErrorFrom(failure: unknown): Promise<AttachmentError> {
  if (failure instanceof AttachmentError) return failure;
  if (!(failure instanceof HttpErrorResponse)) return clientAttachmentError();
  const parsed = parseApiError({ status: failure.status, error: await decodedBody(failure.error) });
  return new AttachmentError(parsed.code, parsed.referenceCode ?? null, parsed.status);
}

/** An operator body for `catchError`: rethrows `failure` as an {@link AttachmentError}. */
export function rethrowAsAttachmentError(failure: unknown): Observable<never> {
  return from(attachmentErrorFrom(failure)).pipe(concatMap((error) => throwError(() => error)));
}

async function decodedBody(body: unknown): Promise<unknown> {
  if (body instanceof Blob) return parsedJson(await body.text());
  if (typeof body === 'string') return parsedJson(body);
  return body;
}

/** The JSON value of `text`, or `text` itself when it is not JSON (a proxy's HTML page stays a string). */
function parsedJson(text: string): unknown {
  try {
    return JSON.parse(text) as unknown;
  } catch (error) {
    if (error instanceof SyntaxError) return text;
    throw error;
  }
}
