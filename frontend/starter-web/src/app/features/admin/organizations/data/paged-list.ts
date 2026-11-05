import { signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom, type Observable } from 'rxjs';
import { apiErrorFrom } from './admin-api-error';

/** A page of an admin list as the API sends it (`total` is an int32, typed `number | string` by the generator). */
export interface ApiPage<TItem> {
  readonly items: readonly TItem[];
  readonly total: number | string;
}

/**
 * The rows, total, loading flag and load failure of one paged admin list, for the admin-list pattern's inputs.
 * {@link load} keeps only the answer of the latest request, so a slow earlier page never overwrites a newer one.
 * A failed load clears the rows and the total, so a list the user may no longer read never stays on screen.
 */
export class PagedListState<TQuery, TItem> {
  private readonly rows = signal<readonly TItem[]>([]);
  private readonly count = signal(0);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);
  private sequence = 0;

  readonly items = this.rows.asReadonly();
  readonly total = this.count.asReadonly();
  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();

  constructor(private readonly loadPage: (query: TQuery) => Observable<ApiPage<TItem>>) {}

  async load(query: TQuery): Promise<void> {
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const page = await firstValueFrom(this.loadPage(query));
      if (current === this.sequence) {
        this.rows.set(page.items);
        this.count.set(Number(page.total));
      }
    } catch (error: unknown) {
      if (current === this.sequence) {
        this.rows.set([]);
        this.count.set(0);
        this.failure.set(apiErrorFrom(error));
      }
    } finally {
      if (current === this.sequence) {
        this.busy.set(false);
      }
    }
  }
}

/** The active-state filter of an admin list. */
export enum ActiveFilter {
  All = 1,
  Active = 2,
  Inactive = 3,
}

export const ACTIVE_FILTERS: readonly ActiveFilter[] = [ActiveFilter.All, ActiveFilter.Active, ActiveFilter.Inactive];

/** The translation key of each active-state filter option. */
export const ActiveFilterLabelKeys: Readonly<Record<ActiveFilter, string>> = {
  [ActiveFilter.All]: 'admin.common.filters.all-statuses',
  [ActiveFilter.Active]: 'admin.common.filters.active',
  [ActiveFilter.Inactive]: 'admin.common.filters.inactive',
};

/** The `isActive` query value of a filter: null sends no filter. */
export function isActiveFor(filter: ActiveFilter): boolean | null {
  if (filter === ActiveFilter.Active) {
    return true;
  }
  return filter === ActiveFilter.Inactive ? false : null;
}

/**
 * The value of a native `<select>` option as a number from `allowed`, or null for the "all" option (an empty value)
 * and for anything else. Native selects only carry strings, so the filters parse them here.
 */
export function selectedNumber<T extends number>(value: string, allowed: readonly T[]): T | null {
  if (value === '') {
    return null;
  }
  const parsed = Number(value);
  return (allowed as readonly number[]).includes(parsed) ? (parsed as T) : null;
}

/** The value of the event's `<select>` (filters and pickers bind `(change)`). */
export function selectValue(event: Event): string {
  return (event.target as HTMLSelectElement | null)?.value ?? '';
}
