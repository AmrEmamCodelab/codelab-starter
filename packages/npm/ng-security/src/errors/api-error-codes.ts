/**
 * The platform error codes. Mirrors Codelab.Errors' `ErrorCodes` (the codes its failure classifiers
 * and framework status mapping emit). Module-specific codes belong to the app, which passes them to
 * `apiErrorMessageKey` with its translated set.
 */
export const ApiErrorCodes = Object.freeze({
  validation: 'VALIDATION_FAILED',
  malformed: 'MALFORMED_REQUEST',
  invalidFile: 'INVALID_FILE',
  businessRule: 'BUSINESS_RULE_VIOLATED',
  unauthenticated: 'UNAUTHENTICATED',
  forbidden: 'FORBIDDEN',
  notFound: 'NOT_FOUND',
  concurrency: 'CONCURRENCY',
  retryable: 'RETRYABLE',
  upstream: 'UPSTREAM_UNAVAILABLE',
  internalError: 'INTERNAL_ERROR',
  /** Client-side only: the request never reached a server (status 0). */
  offline: 'NETWORK',
} as const);

export type ApiErrorCode = (typeof ApiErrorCodes)[keyof typeof ApiErrorCodes];

/** HTTP statuses the status mapping names. */
export const HttpStatus = Object.freeze({
  noResponse: 0,
  badRequest: 400,
  unauthorized: 401,
  forbidden: 403,
  notFound: 404,
  methodNotAllowed: 405,
  conflict: 409,
  payloadTooLarge: 413,
  unsupportedMediaType: 415,
  unprocessableEntity: 422,
  serverError: 500,
  badGateway: 502,
  serviceUnavailable: 503,
} as const);

const STATUS_CODES: ReadonlyMap<number, ApiErrorCode> = new Map<number, ApiErrorCode>([
  [HttpStatus.noResponse, ApiErrorCodes.offline],
  [HttpStatus.badRequest, ApiErrorCodes.malformed],
  [HttpStatus.unauthorized, ApiErrorCodes.unauthenticated],
  [HttpStatus.forbidden, ApiErrorCodes.forbidden],
  [HttpStatus.notFound, ApiErrorCodes.notFound],
  [HttpStatus.methodNotAllowed, ApiErrorCodes.malformed],
  [HttpStatus.unsupportedMediaType, ApiErrorCodes.malformed],
  [HttpStatus.conflict, ApiErrorCodes.concurrency],
  [HttpStatus.payloadTooLarge, ApiErrorCodes.invalidFile],
  [HttpStatus.unprocessableEntity, ApiErrorCodes.businessRule],
  [HttpStatus.badGateway, ApiErrorCodes.upstream],
  [HttpStatus.serviceUnavailable, ApiErrorCodes.retryable],
]);

/**
 * The code for a response that carried none: the same table the server's framework status mapping
 * uses, so a body from a proxy and a body from the API land on the same branch.
 */
export function codeForStatus(status: number): ApiErrorCode {
  const mapped = STATUS_CODES.get(status);
  if (mapped !== undefined) return mapped;
  return status >= HttpStatus.serverError ? ApiErrorCodes.internalError : ApiErrorCodes.malformed;
}

/** No response at all, or a 5xx: a failure the user must be able to report. */
export function isUnlabelledOutage(status: number): boolean {
  return status === HttpStatus.noResponse || status >= HttpStatus.serverError;
}
