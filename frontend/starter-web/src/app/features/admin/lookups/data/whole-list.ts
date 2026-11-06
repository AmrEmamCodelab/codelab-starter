import { signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom, type Observable } from 'rxjs';
import { apiErrorFrom } from '../../organizations/data/admin-api-error';

/**
 * Rows / loading / error of one list the server answers whole (no paging). Only the latest request's answer is
 * kept, so a slow earlier answer never overwrites a newer one; a failed load clears the rows and keeps the refusal.
 */
export class WholeListState<TQuery, TRow> {
  private latest = 0;
  private readonly rows = signal<readonly TRow[]>([]);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);

  readonly items = this.rows.asReadonly();
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();

  constructor(private readonly source: (query: TQuery) => Observable<readonly TRow[]>) {}

  async load(query: TQuery): Promise<void> {
    const request = ++this.latest;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const rows = await firstValueFrom(this.source(query));
      if (request === this.latest) {
        this.rows.set(rows);
      }
    } catch (error: unknown) {
      if (request === this.latest) {
        this.rows.set([]);
        this.failure.set(apiErrorFrom(error));
      }
    } finally {
      if (request === this.latest) {
        this.busy.set(false);
      }
    }
  }
}
