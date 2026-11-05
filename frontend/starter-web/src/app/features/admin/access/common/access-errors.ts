import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorCodes, HttpStatus, isResponseSignatureError, parseApiError, type ApiError } from '@codelab/ng-security';
import { AccessErrorCodes } from '../data/access.models';

/**
 * The problem+json refusal of a failed API call, parsed by `@codelab/ng-security`. An answer whose signature could not
 * be verified becomes a refusal with no server detail: `FORBIDDEN` when it carried 403, else `INTERNAL_ERROR`. Anything
 * else is a programming error and is rethrown, never turned into a message.
 */
export function apiErrorFrom(error: unknown): ApiError {
  if (error instanceof HttpErrorResponse) {
    return parseApiError(error);
  }
  if (isResponseSignatureError(error)) {
    const forbidden = error.status === HttpStatus.forbidden;
    return { status: error.status, code: forbidden ? ApiErrorCodes.forbidden : ApiErrorCodes.internalError, fieldErrors: [] };
  }
  throw error;
}

/** True for the self-lockout refusal that a confirmed resend (`confirmBreakGlassOnly: true`) may override. */
export function isLastManagerRefusal(error: ApiError | null): boolean {
  return error?.serverCode === AccessErrorCodes.LastManager;
}

/** The translation key of an error or warning code (`errors.<CODE>`). */
export function errorMessageKey(code: string): string {
  return `errors.${code}`;
}
