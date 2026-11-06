import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { quietContext } from '../../core/http/http-context';
import { mineParams, oversightParams } from './record-query';
import { RecordsApiPaths } from './records-api';
import type {
  MineQuery,
  OversightListItem,
  OversightQuery,
  RecordDetail,
  RecordId,
  RecordListItem,
  RecordPage,
  TimelineEntry,
  TransitionRequest,
} from './records.models';

/** One workflow action to apply: the action key from the record's `allowedActions`, and the reason when one is given. */
export interface TransitionCommand {
  readonly action: string;
  readonly reason?: string | null;
}

/**
 * The generic record endpoints for ANY record type (Records module), a thin wrapper over `ApiService` (bearer token,
 * request signing). Every call is quiet: record screens show failures in their own error state or dialog (no app-wide
 * error dialog), while a 401 still ends an expired session. Failures arrive as the raw `HttpErrorResponse`; turn them
 * into an `ApiError` with `recordsApiErrorFrom`.
 *
 * Authorization is the server's: `mine` and `detail` need the type's view-own (or queue) permission, `oversight`
 * needs `records.oversight.view`, and `transition` is refused unless the action is in the record's `allowedActions`.
 */
@Injectable({ providedIn: 'root' })
export class RecordsClient {
  private readonly api = inject(ApiService);

  /** The caller's own records of `recordType`, newest first (server order). */
  mine(recordType: string, query: MineQuery = {}): Observable<RecordPage<RecordListItem>> {
    return this.api.get<RecordPage<RecordListItem>>(RecordsApiPaths.mine(recordType), {
      params: mineParams(query),
      context: quietContext(),
    });
  }

  /** One record, with the allowed actions computed for the caller's audience (empty when terminal). 404 `RECORDS_NOT_FOUND`. */
  detail(recordType: string, id: RecordId): Observable<RecordDetail> {
    return this.api.get<RecordDetail>(RecordsApiPaths.record(recordType, id), { context: quietContext() });
  }

  /** The record's transition history, in the server's order (the timeline component sorts it). */
  timeline(recordType: string, id: RecordId): Observable<readonly TimelineEntry[]> {
    return this.api.get<readonly TimelineEntry[]>(RecordsApiPaths.timeline(recordType, id), { context: quietContext() });
  }

  /**
   * Applies one workflow action and answers the record as it is now. A blank reason is not sent. Refusals:
   * 400 `WORKFLOW_REASON_REQUIRED` / `WORKFLOW_REASON_TOO_LONG`, 403 `WORKFLOW_FORBIDDEN`, 404 `WORKFLOW_ACTION_UNKNOWN`
   * or `RECORDS_NOT_FOUND`, 409 `WORKFLOW_ACTION_NOT_ALLOWED` (the record moved on: reload it).
   */
  transition(recordType: string, id: RecordId, command: TransitionCommand): Observable<RecordDetail> {
    const reason = command.reason?.trim();
    const body: TransitionRequest = reason ? { action: command.action, reason } : { action: command.action };
    return this.api.post<RecordDetail>(RecordsApiPaths.transitions(recordType, id), body, { context: quietContext() });
  }

  /** Every record of `recordType`, read-only, for administrators (`records.oversight.view`). */
  oversight(recordType: string, query: OversightQuery = {}): Observable<RecordPage<OversightListItem>> {
    return this.api.get<RecordPage<OversightListItem>>(RecordsApiPaths.oversight(recordType), {
      params: oversightParams(query),
      context: quietContext(),
    });
  }
}
