/**
 * ONE parsed shape for every error the Codelab API returns.
 *
 * The server writes one envelope (Codelab.Errors): RFC 9457 problem+json with `code`,
 * `correlationId` and `referenceCode`, plus an `errors` dictionary on validation failures. Earlier
 * systems grew several envelopes and every screen needed a parser per shape; the starter has one,
 * so this parser reads one (and degrades to the status for a body that is not the envelope, such as
 * a proxy's HTML error page).
 *
 * Takes any `{ status, error }` value, so an Angular `HttpErrorResponse` fits without this module
 * importing Angular.
 */

import { ApiErrorCodes, codeForStatus, isUnlabelledOutage } from './api-error-codes.ts';
import { isClientTraceId, referenceCodeFrom } from './reference-code.ts';

export { ApiErrorCodes } from './api-error-codes.ts';

/** The parts of an HTTP error response this parser reads. */
export interface HttpErrorLike {
  /** 0 for a network or CORS failure, where there is no response at all. */
  readonly status?: number;
  /** The decoded body. */
  readonly error?: unknown;
}

/** One field error, keyed by the camelCase dotted path forms bind by. */
export interface ApiFieldError {
  readonly field: string;
  readonly message: string;
}

/** The one parsed error shape. */
export interface ApiError {
  readonly status: number;
  /** What to RENDER from (always populated; derived from the status when the server sent none). */
  readonly code: string;
  /**
   * The code the server actually sent, or undefined. BRANCH on this, never on `code`: a dialog that
   * offers a specific affordance must not fire for every 409 whose code was derived.
   */
  readonly serverCode?: string;
  /** The server's trace id, or a client-minted one for an unlabelled outage (see {@link parseApiError}). */
  readonly correlationId?: string;
  /** The short code the user reads out. Present exactly when `correlationId` is. */
  readonly referenceCode?: string;
  /** Server text in the server's language: a FALLBACK only, never rendered when `code` translates. */
  readonly detail?: string;
  readonly fieldErrors: readonly ApiFieldError[];
}

/** Model-binder JSON paths (`$.address.city`) and property names key alike for the forms. */
const BINDER_PATH_PREFIX = /^\$\.?/;

/** Field errors from a problem+json `errors` dictionary (`{ "field": ["message", ...] }`). */
export function parseFieldErrors(body: unknown): ApiFieldError[] {
  const errors = (body as { errors?: unknown } | null | undefined)?.errors;
  if (errors === null || typeof errors !== 'object' || Array.isArray(errors)) return [];
  return Object.entries(errors as Record<string, unknown>).flatMap(([key, value]) => {
    const field = key.replace(BINDER_PATH_PREFIX, '');
    const messages = Array.isArray(value) ? value : [value];
    return messages
      .filter((message) => message !== null && message !== undefined && String(message).length > 0)
      .map((message) => ({ field, message: String(message) }));
  });
}

/**
 * Parses any error response into the one shape. Never throws and never returns null.
 *
 * `clientTraceId` is the trace id the SPA minted for the request (an interceptor supplies it). It is
 * used ONLY when the failure carries no server id and is one the user must be able to report:
 * status 0, or a 5xx written by something in front of the API. A server id always wins.
 */
export function parseApiError(response: HttpErrorLike, clientTraceId?: string): ApiError {
  const parsed = parseResponse(response);
  if (parsed.correlationId !== undefined || !isUnlabelledOutage(parsed.status) || !isClientTraceId(clientTraceId)) {
    return parsed;
  }
  return { ...parsed, correlationId: clientTraceId, referenceCode: referenceCodeFrom(clientTraceId) };
}

function parseResponse(response: HttpErrorLike): ApiError {
  const status = response?.status ?? 0;
  const body = response?.error;
  if (status === 0) return { status, code: ApiErrorCodes.offline, fieldErrors: [] };
  if (body === null || typeof body !== 'object') return { status, code: codeForStatus(status), fieldErrors: [] };

  const record = body as Record<string, unknown>;
  const serverCode = nonEmptyString(record['code']);
  const correlationId = nonEmptyString(record['correlationId']);
  return {
    status,
    code: serverCode ?? codeForStatus(status),
    serverCode,
    correlationId,
    referenceCode: correlationId === undefined ? undefined : referenceCodeFor(record, correlationId),
    detail: nonEmptyString(record['detail']),
    fieldErrors: parseFieldErrors(body),
  };
}

/** The server's `referenceCode` when it sent one; otherwise the shared rule applied to the id. */
function referenceCodeFor(record: Record<string, unknown>, correlationId: string): string {
  return nonEmptyString(record['referenceCode']) ?? referenceCodeFrom(correlationId);
}

function nonEmptyString(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

/** The i18n key namespace error messages live under. */
export const ERROR_MESSAGE_KEY_PREFIX = 'errors.';

const DEFAULT_TRANSLATED_CODES: ReadonlySet<string> = new Set(Object.values(ApiErrorCodes));

/**
 * The translation key for an error: `errors.<CODE>`, or `errors.INTERNAL_ERROR` when the code has no
 * guaranteed bundle entry. The backstop matters: a translation library renders a missing key as the
 * key itself, so the user would read `errors.SOME_CODE` on screen.
 *
 * @param translatedCodes the codes the app's bundles translate; defaults to {@link ApiErrorCodes}.
 * Pass a superset when the app adds its own codes.
 */
export function apiErrorMessageKey(
  error: ApiError,
  translatedCodes: ReadonlySet<string> = DEFAULT_TRANSLATED_CODES,
): string {
  const code = translatedCodes.has(error.code) ? error.code : ApiErrorCodes.internalError;
  return ERROR_MESSAGE_KEY_PREFIX + code;
}
