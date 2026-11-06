import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom, type Observable } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE, type QueueTab } from '../../../patterns';
import {
  QueuesClient,
  recordsApiErrorFrom,
  type QueueQuery,
  type QueueView,
  type WorkQueueItem,
} from '../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE } from '../data';
import { ServiceRequestsStaffPermissionKeys } from './service-requests-staff.permissions';
import { ServiceRequestsStaffTranslationKeys } from './service-requests-staff-translation-keys';
import { assignmentHolder, type AssignmentHolder } from './staff-decisions';
import { asQueueView, initialQueueView, staffQueueTabs } from './staff-queue-views';

/** A success message after a claim or release: its translation key and the record number it names. */
export interface QueueNotice {
  readonly key: string;
  readonly number: string;
}

/**
 * The state of the staff work queue (provided per page): the view (unassigned / mine / all), the status filter, the
 * number search and the page, the server's rows for them, and claim / release. Every filter change goes back to page
 * 1; the list reloads whenever the query changes, and only the latest request's answer is kept.
 */
@Injectable()
export class ServiceRequestQueueStore {
  private readonly queues = inject(QueuesClient);
  private readonly permissions = inject(PermissionStore);
  private readonly auth = inject(AuthService);
  private sequence = 0;

  readonly pageSize = DEFAULT_PAGE_SIZE;
  readonly canWork = computed(() => this.permissions.has(ServiceRequestsStaffPermissionKeys.Work));
  readonly tabs = computed<readonly QueueTab[]>(() => staffQueueTabs(this.canWork()));

  private readonly viewNow = signal<QueueView>(initialQueueView(untracked(() => this.tabs())));
  private readonly statusNow = signal<number | null>(null);
  private readonly numberNow = signal('');
  private readonly pageNow = signal(FIRST_PAGE);
  private readonly rowsNow = signal<readonly WorkQueueItem[]>([]);
  private readonly totalNow = signal(0);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);
  private readonly workingOn = signal<string | null>(null);
  private readonly actionFailure = signal<ApiError | null>(null);
  private readonly noticeNow = signal<QueueNotice | null>(null);

  readonly view = this.viewNow.asReadonly();
  readonly status = this.statusNow.asReadonly();
  readonly number = this.numberNow.asReadonly();
  readonly page = this.pageNow.asReadonly();
  readonly rows = this.rowsNow.asReadonly();
  readonly total = this.totalNow.asReadonly();
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();
  /** The id of the row a claim or release is running for, or null. */
  readonly workingRowId = this.workingOn.asReadonly();
  /** The refusal of the last claim or release. */
  readonly actionError = this.actionFailure.asReadonly();
  readonly notice = this.noticeNow.asReadonly();

  /** The request the list shows; reading it is what makes the list reload. */
  readonly query = computed<QueueQuery>(() => ({
    view: this.viewNow(),
    status: this.statusNow(),
    number: this.numberNow() || null,
    page: this.pageNow(),
    pageSize: this.pageSize,
  }));

  constructor() {
    effect(() => {
      const query = this.query();
      untracked(() => void this.load(query));
    });
  }

  /** Opens another view (a tab); a value that is not a view is ignored. Back to page 1. */
  setView(value: string): void {
    const view = asQueueView(value);
    if (view !== null && view !== this.viewNow()) {
      this.viewNow.set(view);
      this.pageNow.set(FIRST_PAGE);
    }
  }

  /** Filters by one status (null: any). Back to page 1. */
  setStatus(status: number | null): void {
    if (status !== this.statusNow()) {
      this.statusNow.set(status);
      this.pageNow.set(FIRST_PAGE);
    }
  }

  /** Searches by part of the request number (trimmed; blank: no search). Back to page 1. */
  setNumber(text: string): void {
    const number = text.trim();
    if (number !== this.numberNow()) {
      this.numberNow.set(number);
      this.pageNow.set(FIRST_PAGE);
    }
  }

  /** Opens a page (1-based; anything lower opens the first). */
  setPage(page: number): void {
    this.pageNow.set(Math.max(FIRST_PAGE, page));
  }

  /** Loads the current query again (retry, or after a claim). */
  reload(): Promise<void> {
    return this.load(this.query());
  }

  /** Who holds `row`, seen from the signed-in user. */
  holderOf(row: WorkQueueItem): AssignmentHolder {
    return assignmentHolder(row.assignee, this.auth.user()?.id ?? null);
  }

  /** Takes the record; the list reloads (it leaves "unassigned" and joins "mine"). */
  claim(row: WorkQueueItem): Promise<void> {
    return this.assignment(row, ServiceRequestsStaffTranslationKeys.Queue.Claimed, () =>
      this.queues.claim(SERVICE_REQUEST_RECORD_TYPE, row.id));
  }

  /** Gives the record back to the queue; the list reloads. */
  release(row: WorkQueueItem): Promise<void> {
    return this.assignment(row, ServiceRequestsStaffTranslationKeys.Queue.Released, () =>
      this.queues.release(SERVICE_REQUEST_RECORD_TYPE, row.id));
  }

  dismissNotice(): void {
    this.noticeNow.set(null);
  }

  private async assignment(row: WorkQueueItem, noticeKey: string, call: () => Observable<void>): Promise<void> {
    if (this.workingOn() !== null) {
      return;
    }
    this.workingOn.set(row.id);
    this.actionFailure.set(null);
    this.noticeNow.set(null);
    try {
      await firstValueFrom(call(), { defaultValue: undefined });
      this.noticeNow.set({ key: noticeKey, number: row.number });
    } catch (error: unknown) {
      this.actionFailure.set(recordsApiErrorFrom(error));
    } finally {
      this.workingOn.set(null);
    }
    // Either way the row may have moved on (claimed by someone else first): show the queue as it is now.
    await this.reload();
  }

  private async load(query: QueueQuery): Promise<void> {
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const page = await firstValueFrom(this.queues.list(SERVICE_REQUEST_RECORD_TYPE, query));
      if (current === this.sequence) {
        this.rowsNow.set(page.items);
        this.totalNow.set(page.total);
      }
    } catch (error: unknown) {
      if (current === this.sequence) {
        this.rowsNow.set([]);
        this.totalNow.set(0);
        this.failure.set(recordsApiErrorFrom(error));
      }
    } finally {
      if (current === this.sequence) {
        this.busy.set(false);
      }
    }
  }
}
