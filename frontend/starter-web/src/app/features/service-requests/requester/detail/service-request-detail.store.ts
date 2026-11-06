import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { ApiError } from '@codelab/ng-security';
import { forkJoin, map, of, switchMap, type Observable } from 'rxjs';
import {
  RecordsClient,
  recordsApiErrorFrom,
  type RecordDecision,
  type RecordDetail,
  type TimelineEntry,
} from '../../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE, ServiceRequestsClient, type ServiceRequestDetails, type ServiceRequestId } from '../../data';
import { isDraftStatus } from '../requester-status';

/** Everything the requester detail page shows of one request. `details` is null for a draft (it has no fields yet). */
export interface RequesterRequestView {
  readonly record: RecordDetail;
  readonly timeline: readonly TimelineEntry[];
  readonly details: ServiceRequestDetails | null;
}

/**
 * The state of the requester detail page (provided by the page): the generic record (status, wording, allowed actions,
 * last reason), its timeline and the request's own fields, loaded together; and the requester's decisions (cancel,
 * resubmit) sent through the generic transitions endpoint. A decision answers the record as it is now; the timeline
 * is then read again.
 */
@Injectable()
export class ServiceRequestDetailStore {
  private readonly records = inject(RecordsClient);
  private readonly client = inject(ServiceRequestsClient);
  private readonly destroyRef = inject(DestroyRef);
  private currentId: ServiceRequestId | null = null;

  private readonly viewState = signal<RequesterRequestView | null>(null);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<ApiError | null>(null);
  private readonly busyState = signal(false);
  private readonly actionErrorState = signal<ApiError | null>(null);

  readonly view = this.viewState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();
  /** A decision is being sent (the decision panel keeps its dialog open meanwhile). */
  readonly busy = this.busyState.asReadonly();
  /** The refusal of the last decision (shown in the decision panel's dialog). */
  readonly actionError = this.actionErrorState.asReadonly();

  load(id: ServiceRequestId): void {
    this.currentId = id;
    this.loadingState.set(true);
    this.errorState.set(null);
    forkJoin({
      record: this.records.detail(SERVICE_REQUEST_RECORD_TYPE, id),
      timeline: this.records.timeline(SERVICE_REQUEST_RECORD_TYPE, id),
    })
      .pipe(
        switchMap(({ record, timeline }) => this.withDetails(id, record).pipe(map((details) => ({ record, timeline, details })))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (view) => {
          this.viewState.set(view);
          this.loadingState.set(false);
        },
        error: (failure: unknown) => {
          this.errorState.set(recordsApiErrorFrom(failure));
          this.loadingState.set(false);
        },
      });
  }

  /** Loads the request again (the page's Retry). */
  reload(): void {
    if (this.currentId !== null) this.load(this.currentId);
  }

  /**
   * Sends one of the record's allowed actions. `busy` is set synchronously (the decision panel waits for it to fall
   * again with no error before it closes its dialog). On success the record and its timeline are replaced.
   */
  decide(decision: RecordDecision): void {
    const id = this.currentId;
    const view = this.viewState();
    if (id === null || view === null) return;
    this.busyState.set(true);
    this.actionErrorState.set(null);
    this.records
      .transition(SERVICE_REQUEST_RECORD_TYPE, id, { action: decision.actionKey, reason: decision.reason })
      .pipe(
        switchMap((record) => this.records.timeline(SERVICE_REQUEST_RECORD_TYPE, id).pipe(map((timeline) => ({ record, timeline })))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: ({ record, timeline }) => {
          this.viewState.set({ ...view, record, timeline });
          this.busyState.set(false);
        },
        error: (failure: unknown) => {
          this.actionErrorState.set(recordsApiErrorFrom(failure));
          this.busyState.set(false);
        },
      });
  }

  private withDetails(id: ServiceRequestId, record: RecordDetail): Observable<ServiceRequestDetails | null> {
    return isDraftStatus(record.status) ? of(null) : this.client.get(id);
  }
}
