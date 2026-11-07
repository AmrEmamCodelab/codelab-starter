import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom, forkJoin, type Observable } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import type { AllowedAction } from '../../../patterns';
import {
  QueuesClient,
  RecordsClient,
  WorkflowErrorCodes,
  recordsApiErrorFrom,
  type RecordAllowedAction,
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
import { SERVICE_REQUEST_NOT_FOUND, serviceRequestIdFromRoute } from './service-request-route-id';
import { AssignmentHolder, assignmentHolder, findRecordAction, staffDecisionButtons } from './staff-decisions';

/** Everything the staff detail shows of one request. */
interface LoadedRequest {
  readonly detail: RecordDetail;
  readonly details: ServiceRequestDetails;
  readonly timeline: readonly TimelineEntry[];
  /** Where the files live (the record's Records-kernel attachments id), read once on load; null when it carried none. */
  readonly attachments: ServiceRequestAttachmentsTarget | null;
}

/**
 * The state of the staff detail of one service request (provided per page): the generic record (status, wording,
 * allowed actions, assignee, SLA), the request's own fields, the timeline, claim / release, and the decisions.
 *
 * - The decision buttons are the server's `allowedActions` and nothing else; a terminal record has none.
 * - An action whose reason is Required opens the decision dialog (`pending`); the dialog blocks a blank reason, the
 *   server re-checks it. The others are sent at once.
 * - After a decision the record is the server's answer (new status, new allowed actions) and the timeline reloads.
 *   A 409 `WORKFLOW_ACTION_NOT_ALLOWED` (someone else moved the record) reloads the record.
 */
@Injectable()
export class ServiceRequestStaffDetailStore {
  private readonly records = inject(RecordsClient);
  private readonly queues = inject(QueuesClient);
  private readonly requests = inject(ServiceRequestsClient);
  private readonly auth = inject(AuthService);
  private sequence = 0;

  private readonly idNow = signal<ServiceRequestId | null>(null);
  private readonly loaded = signal<LoadedRequest | null>(null);
  private readonly busy = signal(false);
  private readonly loadFailure = signal<ApiError | null>(null);
  private readonly assigningNow = signal(false);
  private readonly assignFailure = signal<ApiError | null>(null);
  private readonly decidingNow = signal(false);
  private readonly decideFailure = signal<ApiError | null>(null);
  private readonly pendingNow = signal<RecordAllowedAction | null>(null);
  private readonly decidedNow = signal<string | null>(null);

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
  readonly loadError = this.loadFailure.asReadonly();
  readonly assigning = this.assigningNow.asReadonly();
  readonly assignError = this.assignFailure.asReadonly();
  readonly deciding = this.decidingNow.asReadonly();
  readonly decideError = this.decideFailure.asReadonly();
  /** The action waiting for its reason in the decision dialog, or null. */
  readonly pending = this.pendingNow.asReadonly();
  /** The label key of the decision just recorded, or null. */
  readonly decided = this.decidedNow.asReadonly();
  /** The reason as typed in the decision dialog. */
  readonly reason = signal('');

  /** The decision buttons: the server's allowed actions (empty when terminal). */
  readonly decisionButtons = computed<readonly AllowedAction[]>(() => {
    const detail = this.detail();
    return detail === null || detail.isTerminal ? [] : staffDecisionButtons(detail.allowedActions);
  });
  /** Who holds the request, seen from the signed-in user. */
  readonly holder = computed(() => {
    const detail = this.detail();
    return detail === null ? null : assignmentHolder(detail.assignee, this.auth.user()?.id ?? null);
  });
  /** Claim / release apply only while the request is open. */
  readonly canClaim = computed(() => this.holder() === AssignmentHolder.Nobody && this.detail()?.isTerminal === false);
  readonly canRelease = computed(() => this.holder() === AssignmentHolder.CurrentUser && this.detail()?.isTerminal === false);

  /** Opens the request with the route's `id`; an id that is not a PublicId shows "not found" without a call. */
  open(rawId: string): Promise<void> {
    const id = serviceRequestIdFromRoute(rawId);
    this.idNow.set(id);
    this.pendingNow.set(null);
    this.decidedNow.set(null);
    if (id === null) {
      this.loaded.set(null);
      this.loadFailure.set(SERVICE_REQUEST_NOT_FOUND);
      return Promise.resolve();
    }
    return this.reload();
  }

  /** Loads the record, the request's fields and the timeline again. */
  async reload(): Promise<void> {
    const id = this.idNow();
    if (id === null) {
      return;
    }
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.loadFailure.set(null);
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
        this.loadFailure.set(recordsApiErrorFrom(error));
      }
    } finally {
      if (current === this.sequence) {
        this.busy.set(false);
      }
    }
  }

  /** Takes the request; the record reloads, so the decisions the server now allows appear. */
  claim(): Promise<void> {
    return this.assignment((id) => this.queues.claim(SERVICE_REQUEST_RECORD_TYPE, id));
  }

  /** Gives the request back to the queue; the record reloads. */
  release(): Promise<void> {
    return this.assignment((id) => this.queues.release(SERVICE_REQUEST_RECORD_TYPE, id));
  }

  /**
   * A decision button was pressed: a Required reason opens the dialog, any other action is sent at once. An action
   * the server no longer lists is ignored.
   */
  choose(button: AllowedAction): Promise<void> {
    const action = findRecordAction(this.detail()?.allowedActions ?? [], button.code);
    if (action === null || this.decidingNow()) {
      return Promise.resolve();
    }
    this.decideFailure.set(null);
    this.decidedNow.set(null);
    if (button.requiresReason) {
      this.reason.set('');
      this.pendingNow.set(action);
      return Promise.resolve();
    }
    return this.decide(action, null);
  }

  /** The dialog's confirm: sends the pending action with the (already trimmed, non-blank) reason. */
  confirmReason(reason: string): Promise<void> {
    const action = this.pendingNow();
    return action === null ? Promise.resolve() : this.decide(action, reason);
  }

  /** The dialog's cancel: nothing is sent. */
  cancelReason(): void {
    if (!this.decidingNow()) {
      this.pendingNow.set(null);
      this.decideFailure.set(null);
    }
  }

  private async decide(action: RecordAllowedAction, reason: string | null): Promise<void> {
    const id = this.idNow();
    if (id === null) {
      return;
    }
    this.decidingNow.set(true);
    this.decideFailure.set(null);
    try {
      const detail = await firstValueFrom(
        this.records.transition(SERVICE_REQUEST_RECORD_TYPE, id, { action: action.actionKey, reason }));
      this.pendingNow.set(null);
      this.decidedNow.set(action.labelKey);
      this.loaded.update((loaded) => (loaded === null ? null : { ...loaded, detail }));
      await this.refreshTimeline(id);
    } catch (error: unknown) {
      const failure = recordsApiErrorFrom(error);
      this.decideFailure.set(failure);
      if (failure.code === WorkflowErrorCodes.ActionNotAllowed) {
        this.pendingNow.set(null);
        await this.reload();
      }
    } finally {
      this.decidingNow.set(false);
    }
  }

  private async refreshTimeline(id: ServiceRequestId): Promise<void> {
    try {
      const timeline = await firstValueFrom(this.records.timeline(SERVICE_REQUEST_RECORD_TYPE, id));
      this.loaded.update((loaded) => (loaded === null ? null : { ...loaded, timeline }));
    } catch {
      // The decision is recorded; a timeline that failed to refresh shows on the next load.
    }
  }

  private async assignment(call: (id: ServiceRequestId) => Observable<void>): Promise<void> {
    const id = this.idNow();
    if (id === null || this.assigningNow()) {
      return;
    }
    this.assigningNow.set(true);
    this.assignFailure.set(null);
    try {
      await firstValueFrom(call(id), { defaultValue: undefined });
    } catch (error: unknown) {
      this.assignFailure.set(recordsApiErrorFrom(error));
    } finally {
      this.assigningNow.set(false);
    }
    await this.reload();
  }
}
