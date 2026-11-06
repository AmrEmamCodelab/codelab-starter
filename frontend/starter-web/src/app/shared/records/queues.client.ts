import { Injectable, inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { quietContext } from '../../core/http/http-context';
import { queueParams } from './record-query';
import { QueuesApiPaths } from './records-api';
import type { AssignRequest, QueueQuery, RecordId, RecordPage, RecordUserId, WorkQueueItem } from './records.models';

/**
 * The work queue endpoints for ANY record type (Assignments module), a thin wrapper over `ApiService`. Every call is
 * quiet: the queue screen shows its own failures. Seeing a queue needs the record type's queue permission;
 * `claim`/`release` need `assignments.work`, `assign` needs `assignments.assign`.
 *
 * Refusals: 409 `ASSIGNMENTS_ALREADY_CLAIMED`, 403 `ASSIGNMENTS_NOT_ASSIGNEE`, 400 `ASSIGNMENTS_ASSIGNEE_NOT_ELIGIBLE`.
 */
@Injectable({ providedIn: 'root' })
export class QueuesClient {
  private readonly api = inject(ApiService);

  /** One page of the queue of `recordType` in the given view, filtered. */
  list(recordType: string, query: QueueQuery): Observable<RecordPage<WorkQueueItem>> {
    return this.api.get<RecordPage<WorkQueueItem>>(QueuesApiPaths.queue(recordType), {
      params: queueParams(query),
      context: quietContext(),
    });
  }

  /** The caller takes the record (204). */
  claim(recordType: string, id: RecordId): Observable<void> {
    return this.api.post<void>(QueuesApiPaths.claim(recordType, id), null, { context: quietContext() });
  }

  /** The caller gives the record back to the queue (204). */
  release(recordType: string, id: RecordId): Observable<void> {
    return this.api.post<void>(QueuesApiPaths.release(recordType, id), null, { context: quietContext() });
  }

  /** Hands the record to `userId` (204). */
  assign(recordType: string, id: RecordId, userId: RecordUserId): Observable<void> {
    const body: AssignRequest = { userId };
    return this.api.post<void>(QueuesApiPaths.assign(recordType, id), body, { context: quietContext() });
  }
}
