import { Injectable, computed, signal } from '@angular/core';
import { apiErrorMessageKey, type ApiError } from '@codelab/ng-security';

/**
 * The API failure the error dialog shows. Core holds the state; the layout renders the dialog
 * (`@codelab/ng-dga`) from {@link current}, {@link messageKey} and {@link referenceCode}, and calls
 * {@link dismiss}. One failure at a time: a later one replaces the earlier.
 */
@Injectable({ providedIn: 'root' })
export class ErrorDialogStore {
  private readonly error = signal<ApiError | null>(null);

  readonly current = this.error.asReadonly();
  /** The translation key of the message (`errors.<CODE>`, with a safe fallback). */
  readonly messageKey = computed(() => {
    const error = this.error();
    return error ? apiErrorMessageKey(error) : null;
  });
  /** The short code the user reads out to support, when the failure has one. */
  readonly referenceCode = computed(() => this.error()?.referenceCode ?? null);

  show(error: ApiError): void {
    this.error.set(error);
  }

  dismiss(): void {
    this.error.set(null);
  }
}
