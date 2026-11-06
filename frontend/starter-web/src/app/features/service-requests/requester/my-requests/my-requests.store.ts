import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { ApiError } from '@codelab/ng-security';
import { type Subscription } from 'rxjs';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../../patterns';
import { RecordsClient, recordsApiErrorFrom, type RecordListItem, type RecordPage } from '../../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE, type ServiceRequestStatus } from '../../data';

/** What one list request asks for: the 1-based page and the status filter (null: every status). */
export interface MyRequestsQuery {
  readonly page: number;
  readonly status: ServiceRequestStatus | null;
}

const EMPTY_PAGE: RecordPage<RecordListItem> = Object.freeze({ items: [], page: FIRST_PAGE, pageSize: DEFAULT_PAGE_SIZE, total: 0 });

/**
 * The state of "my requests" (provided by the page): one page of the caller's own service requests from
 * `GET api/records/service-request/mine`, newest first (server order). A new query cancels the one still running, so a
 * slow answer for an old page never overwrites the current one.
 */
@Injectable()
export class MyRequestsStore {
  private readonly records = inject(RecordsClient);
  private readonly destroyRef = inject(DestroyRef);
  private running: Subscription | null = null;
  private lastQuery: MyRequestsQuery = { page: FIRST_PAGE, status: null };

  private readonly pageState = signal<RecordPage<RecordListItem>>(EMPTY_PAGE);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<ApiError | null>(null);

  readonly result = this.pageState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();
  readonly pageSize = DEFAULT_PAGE_SIZE;

  load(query: MyRequestsQuery): void {
    this.lastQuery = query;
    this.running?.unsubscribe();
    this.loadingState.set(true);
    this.errorState.set(null);
    this.running = this.records
      .mine(SERVICE_REQUEST_RECORD_TYPE, { status: query.status, page: query.page, pageSize: this.pageSize })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (page) => {
          this.pageState.set(page);
          this.loadingState.set(false);
        },
        error: (failure: unknown) => {
          this.pageState.set(EMPTY_PAGE);
          this.errorState.set(recordsApiErrorFrom(failure));
          this.loadingState.set(false);
        },
      });
  }

  /** Loads the last query again (the list's Retry). */
  reload(): void {
    this.load(this.lastQuery);
  }
}
