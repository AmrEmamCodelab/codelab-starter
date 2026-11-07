import { HttpErrorResponse } from '@angular/common/http';
import { isClientTraceId, parseApiError, referenceCodeFrom, type ApiError } from '@codelab/ng-security';

/**
 * The response header the API echoes its correlation id in (Codelab.Observability `CorrelationId.Header`). Header
 * names are case-insensitive; Angular's `HttpHeaders` compares them that way.
 */
export const CORRELATION_ID_HEADER = 'X-Correlation-ID';

/**
 * Parses an API failure into the one `ApiError` shape, reference code included.
 *
 * The problem+json body (Codelab.Errors: `code`, `correlationId`, `referenceCode`) always wins. When the body carries
 * no correlation id (a body-less 404 or 413, a proxy's HTML page in front of the API) the response's
 * {@link CORRELATION_ID_HEADER} supplies it, so the user still has a code to read out; the code is derived with the
 * server's rule (`referenceCodeFrom`). A header that is not a 32-hex trace id is ignored: a code that matches no log
 * line is worse than none. Reading the header cross-origin needs the API's CORS policy to expose it.
 */
export function parseHttpFailure(error: HttpErrorResponse): ApiError {
  const parsed = parseApiError(error);
  if (parsed.correlationId !== undefined) {
    return parsed;
  }
  const headerId = error.headers?.get(CORRELATION_ID_HEADER)?.trim().toLowerCase();
  if (!isClientTraceId(headerId)) {
    return parsed;
  }
  return { ...parsed, correlationId: headerId, referenceCode: referenceCodeFrom(headerId) };
}

/**
 * The reference code of a failure, for a page-feedback error state or any self-handled failure: from an
 * `HttpErrorResponse` (body, then header), from an already parsed `ApiError`, or null for anything else (a thrown
 * `Error`, a cancelled call). A feature passes it as a translation parameter of its "failed, quote this code" message.
 */
export function referenceCodeOf(failure: unknown): string | null {
  if (failure instanceof HttpErrorResponse) {
    return parseHttpFailure(failure).referenceCode ?? null;
  }
  if (isApiError(failure)) {
    return failure.referenceCode ?? null;
  }
  return null;
}

function isApiError(value: unknown): value is ApiError {
  if (value === null || typeof value !== 'object') {
    return false;
  }
  const candidate = value as Partial<ApiError>;
  return typeof candidate.status === 'number' && typeof candidate.code === 'string' && Array.isArray(candidate.fieldErrors);
}
