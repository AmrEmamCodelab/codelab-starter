import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import { RecordsClient, recordsApiErrorFrom, type OversightListItem, type OversightQuery } from '../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE } from '../data';

/**
 * The state of the admin "all requests" list (provided per page): the status filter, the number search, the page and
 * the server's rows for them (`GET api/records/service-request/oversight`). Read-only: it has no write method at all.
 * Every filter change goes back to page 1; only the latest request's answer is kept.
 */
@Injectable()
export class ServiceRequestOversightStore {
  private readonly records = inject(RecordsClient);
  private sequence = 0;

  readonly pageSize = DEFAULT_PAGE_SIZE;

  private readonly statusNow = signal<number | null>(null);
  private readonly numberNow = signal('');
  private readonly pageNow = signal(FIRST_PAGE);
  private readonly rowsNow = signal<readonly OversightListItem[]>([]);
  private readonly totalNow = signal(0);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);

  readonly status = this.statusNow.asReadonly();
  readonly number = this.numberNow.asReadonly();
  readonly page = this.pageNow.asReadonly();
  readonly rows = this.rowsNow.asReadonly();
  readonly total = this.totalNow.asReadonly();
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();

  /** The request the list shows; reading it is what makes the list reload. */
  readonly query = computed<OversightQuery>(() => ({
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

  reload(): Promise<void> {
    return this.load(this.query());
  }

  private async load(query: OversightQuery): Promise<void> {
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const page = await firstValueFrom(this.records.oversight(SERVICE_REQUEST_RECORD_TYPE, query));
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
