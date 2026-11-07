import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom, forkJoin } from 'rxjs';
import {
  RecordsClient,
  recordsApiErrorFrom,
  type RecordDetail,
  type TimelineEntry,
} from '../../../shared/records';
import {
  SERVICE_REQUEST_ATTACHMENTS_UNAVAILABLE,
  SERVICE_REQUEST_RECORD_TYPE,
  ServiceRequestsClient,
  serviceRequestAttachmentsTargetOf,
  type ServiceRequestAttachmentsTarget,
  type ServiceRequestDetails,
  type ServiceRequestId,
} from '../data';
import { SERVICE_REQUEST_NOT_FOUND, serviceRequestIdFromRoute } from '../staff/service-request-route-id';

/** Everything the admin detail shows of one request. */
interface LoadedRequest {
  readonly detail: RecordDetail;
  readonly details: ServiceRequestDetails;
  readonly timeline: readonly TimelineEntry[];
  /** Where the files live (the record's Records-kernel attachments id); null when the record carried none. */
  readonly attachments: ServiceRequestAttachmentsTarget | null;
}

/**
 * The state of the admin's read-only detail of one service request (provided per page): the record, the request's
 * own fields, the timeline and where its files live (the attachments id). READ-ONLY by construction: it loads and nothing else (no transition, claim or upload
 * method), and the page never renders the server's allowed actions.
 */
@Injectable()
export class ServiceRequestAdminDetailStore {
  private readonly records = inject(RecordsClient);
  private readonly requests = inject(ServiceRequestsClient);
  private sequence = 0;

  private readonly idNow = signal<ServiceRequestId | null>(null);
  private readonly loaded = signal<LoadedRequest | null>(null);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);

  readonly id = this.idNow.asReadonly();
  readonly detail = computed(() => this.loaded()?.detail ?? null);
  readonly details = computed(() => this.loaded()?.details ?? null);
  readonly timeline = computed<readonly TimelineEntry[]>(() => this.loaded()?.timeline ?? []);
  /** Where the request's files live (`attachmentsRecordId`, never the request id), or null until loaded. */
  readonly attachments = computed(() => this.loaded()?.attachments ?? null);
  /** The refusal shown in place of the attachments: the loaded record carried no usable attachments id. */
  readonly attachmentsError = computed<ApiError | null>(() => {
    const loaded = this.loaded();
    return loaded !== null && loaded.attachments === null ? SERVICE_REQUEST_ATTACHMENTS_UNAVAILABLE : null;
  });
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();

  /** Opens the request with the route's `id`; an id that is not a PublicId shows "not found" without a call. */
  open(rawId: string): Promise<void> {
    const id = serviceRequestIdFromRoute(rawId);
    this.idNow.set(id);
    if (id === null) {
      this.loaded.set(null);
      this.failure.set(SERVICE_REQUEST_NOT_FOUND);
      return Promise.resolve();
    }
    return this.reload();
  }

  async reload(): Promise<void> {
    const id = this.idNow();
    if (id === null) {
      return;
    }
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const loaded = await firstValueFrom(forkJoin({
        detail: this.records.detail(SERVICE_REQUEST_RECORD_TYPE, id),
        details: this.requests.get(id),
        timeline: this.records.timeline(SERVICE_REQUEST_RECORD_TYPE, id),
      }));
      if (current === this.sequence) {
        this.loaded.set({ ...loaded, attachments: serviceRequestAttachmentsTargetOf(loaded.detail) });
      }
    } catch (error: unknown) {
      if (current === this.sequence) {
        this.loaded.set(null);
        this.failure.set(recordsApiErrorFrom(error));
      }
    } finally {
      if (current === this.sequence) {
        this.busy.set(false);
      }
    }
  }
}
