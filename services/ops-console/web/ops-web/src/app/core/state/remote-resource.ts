import { computed, signal } from '@angular/core';
import { parseApiError, type ApiError, type HttpErrorLike } from '@codelab/ng-security';
import type { Observable, Subscription } from 'rxjs';

interface ResourceState<T> {
  readonly data: T | null;
  readonly loading: boolean;
  readonly error: ApiError | null;
}

/**
 * One BFF read as signals: `data`, `loading` and `error` (the parsed problem+json, with its `referenceCode`). A new
 * `load` cancels the pending one, so a slow old answer never overwrites a newer one. A failed reload keeps the last
 * good `data` next to the error; `reset` clears everything.
 *
 * The owner calls `cancel` when it is destroyed.
 */
export class RemoteResource<T> {
  private readonly state = signal<ResourceState<T>>({ data: null, loading: false, error: null });
  private subscription: Subscription | null = null;

  readonly data = computed(() => this.state().data);
  readonly loading = computed(() => this.state().loading);
  readonly error = computed(() => this.state().error);

  load(source: Observable<T>): void {
    this.cancel();
    this.state.update((current) => ({ ...current, loading: true, error: null }));
    this.subscription = source.subscribe({
      next: (data) => this.state.set({ data, loading: false, error: null }),
      error: (error: unknown) =>
        this.state.update((current) => ({ ...current, loading: false, error: parseApiError(error as HttpErrorLike) })),
    });
  }

  reset(): void {
    this.cancel();
    this.state.set({ data: null, loading: false, error: null });
  }

  /** Stops the pending call (if any); its answer is ignored and `loading` ends. */
  cancel(): void {
    this.subscription?.unsubscribe();
    this.subscription = null;
    this.state.update((current) => (current.loading ? { ...current, loading: false } : current));
  }
}
