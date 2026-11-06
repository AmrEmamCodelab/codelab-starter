import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { ApiError } from '@codelab/ng-security';
import { forkJoin, map, type Observable } from 'rxjs';
import { FIRST_PAGE } from '../../../../patterns';
import { RecordsClient, recordsApiErrorFrom, type RecordListItem } from '../../../../shared/records';
import { SERVICE_REQUEST_RECORD_TYPE } from '../../data';
import { RECENT_REQUESTS_COUNT, REQUESTER_DASHBOARD_KPIS, type KpiCounts, type RequesterKpi } from './requester-dashboard';

/** One page is enough to read a total: the dashboard asks for the smallest one. */
const COUNT_PAGE_SIZE = 1;

/**
 * The requester dashboard's state (provided by the page): the total per KPI and the newest requests, all from
 * `GET api/records/service-request/mine` (one call per status figure; the "all" figure comes with the recent list).
 * A failure of any call shows one error with Retry; the figures stay unknown rather than wrong.
 */
@Injectable()
export class RequesterDashboardStore {
  private readonly records = inject(RecordsClient);
  private readonly destroyRef = inject(DestroyRef);

  private readonly countsState = signal<KpiCounts>(new Map());
  private readonly recentState = signal<readonly RecordListItem[]>([]);
  private readonly loadingState = signal(false);
  private readonly errorState = signal<ApiError | null>(null);

  readonly counts = this.countsState.asReadonly();
  readonly recent = this.recentState.asReadonly();
  readonly loading = this.loadingState.asReadonly();
  readonly error = this.errorState.asReadonly();

  load(): void {
    this.loadingState.set(true);
    this.errorState.set(null);
    forkJoin({
      recent: this.records.mine(SERVICE_REQUEST_RECORD_TYPE, { page: FIRST_PAGE, pageSize: RECENT_REQUESTS_COUNT }),
      counts: forkJoin(REQUESTER_DASHBOARD_KPIS.filter((kpi) => kpi.status !== null).map((kpi) => this.countOf(kpi))),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ recent, counts }) => {
          const all = REQUESTER_DASHBOARD_KPIS.find((kpi) => kpi.status === null);
          const entries: [string, number][] = all ? [[all.labelKey, recent.total], ...counts] : counts;
          this.countsState.set(new Map(entries));
          this.recentState.set(recent.items);
          this.loadingState.set(false);
        },
        error: (failure: unknown) => {
          this.countsState.set(new Map());
          this.errorState.set(recordsApiErrorFrom(failure));
          this.loadingState.set(false);
        },
      });
  }

  private countOf(kpi: RequesterKpi): Observable<[string, number]> {
    return this.records
      .mine(SERVICE_REQUEST_RECORD_TYPE, { status: kpi.status, page: FIRST_PAGE, pageSize: COUNT_PAGE_SIZE })
      .pipe(map((page): [string, number] => [kpi.labelKey, page.total]));
  }
}
