import { HttpErrorResponse } from '@angular/common/http';
import { ApiErrorCodes, HttpStatus, isResponseSignatureError, parseApiError, type ApiError } from '@codelab/ng-security';

/** The workflow refusals of a transition (backend `WorkflowErrorCodes`). */
export const WorkflowErrorCodes = {
  /** 409: the action is not allowed from the record's current status (it moved on). */
  ActionNotAllowed: 'WORKFLOW_ACTION_NOT_ALLOWED',
  /** 403: the caller may not take the action. */
  Forbidden: 'WORKFLOW_FORBIDDEN',
  /** 400: the action needs a reason. */
  ReasonRequired: 'WORKFLOW_REASON_REQUIRED',
  /** 400: the reason is longer than the workflow allows. */
  ReasonTooLong: 'WORKFLOW_REASON_TOO_LONG',
  /** 404: no such action in the record type's workflow. */
  ActionUnknown: 'WORKFLOW_ACTION_UNKNOWN',
} as const;

/** The record refusals (backend Records module). */
export const RecordsErrorCodes = {
  /** 404: no such record, or not the caller's. */
  NotFound: 'RECORDS_NOT_FOUND',
  /** 409: the record is not in a submittable state. */
  NotSubmittable: 'RECORDS_NOT_SUBMITTABLE',
  /** 409: someone else changed the record first. */
  Concurrency: 'RECORDS_CONCURRENCY',
} as const;

/** The queue refusals (backend Assignments module). */
export const AssignmentsErrorCodes = {
  /** 409: someone else claimed the record first. */
  AlreadyClaimed: 'ASSIGNMENTS_ALREADY_CLAIMED',
  /** 403: only the assignee may release the record. */
  NotAssignee: 'ASSIGNMENTS_NOT_ASSIGNEE',
  /** 400: the chosen user may not work on the record. */
  AssigneeNotEligible: 'ASSIGNMENTS_ASSIGNEE_NOT_ELIGIBLE',
} as const;

/**
 * Every code the record screens translate (`errors.<CODE>` in both bundles): the generic API codes plus the workflow,
 * record and queue refusals. Pass it as `translatedCodes` to `apiErrorMessageKey` (or a pattern's input).
 */
export const RECORDS_TRANSLATED_CODES: ReadonlySet<string> = new Set<string>([
  ...Object.values(ApiErrorCodes),
  ...Object.values(WorkflowErrorCodes),
  ...Object.values(RecordsErrorCodes),
  ...Object.values(AssignmentsErrorCodes),
]);

/**
 * The problem+json refusal of a failed record or queue call, parsed by `@codelab/ng-security`. An answer whose
 * signature could not be verified becomes a refusal with no server detail: `FORBIDDEN` when it carried 403 (a revoked
 * user sees the access error), else `INTERNAL_ERROR`. Anything else is a programming error and is rethrown.
 */
export function recordsApiErrorFrom(error: unknown): ApiError {
  if (error instanceof HttpErrorResponse) {
    return parseApiError(error);
  }
  if (isResponseSignatureError(error)) {
    const forbidden = error.status === HttpStatus.forbidden;
    return { status: error.status, code: forbidden ? ApiErrorCodes.forbidden : ApiErrorCodes.internalError, fieldErrors: [] };
  }
  throw error;
}
