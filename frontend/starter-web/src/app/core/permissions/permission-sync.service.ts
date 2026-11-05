import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { HttpStatus, parseApiError, type ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { MeClient } from '../api/clients/me.client';
import { PermissionStore } from './permission.store';

/** A sync failure that is not an HTTP response, read as a server fault. */
const NON_HTTP_FAILURE = { status: HttpStatus.serverError } as const;

/**
 * Keeps {@link PermissionStore} in step with the server. Grants and revocations take effect without a
 * new login, so the store is refreshed at login, on start-up with a restored session, and after any 403.
 * Concurrent refreshes share one request.
 */
@Injectable({ providedIn: 'root' })
export class PermissionSyncService {
  private readonly client = inject(MeClient);
  private readonly store = inject(PermissionStore);
  private pendingSync: Promise<void> | null = null;
  private failure: ApiError | null = null;

  /**
   * Why the last {@link ensureLoaded} could not load the keys (the parsed server fault, with its reference
   * code), or null when it loaded. Lets a guard tell "sync failed" apart from "permission missing".
   */
  lastFailure(): ApiError | null {
    return this.failure;
  }

  /**
   * Fetches the user's keys into the store.
   *
   * @throws the HTTP failure when the keys could not be fetched; the store keeps its previous keys.
   */
  refresh(): Promise<void> {
    this.pendingSync ??= firstValueFrom(this.client.permissions())
      .then(response => this.store.set(response.permissions))
      .finally(() => {
        this.pendingSync = null;
      });
    return this.pendingSync;
  }

  /**
   * Resolves once the store holds this session's keys. A failed sync resolves too, leaving the store
   * unloaded, so a guard denies rather than hangs.
   */
  async ensureLoaded(): Promise<boolean> {
    if (this.store.loaded()) {
      this.failure = null;
      return true;
    }
    try {
      await this.refresh();
    } catch (error: unknown) {
      this.failure = parseApiError(error instanceof HttpErrorResponse ? error : NON_HTTP_FAILURE);
      return false;
    }
    this.failure = null;
    return this.store.loaded();
  }

  clear(): void {
    this.failure = null;
    this.store.clear();
  }
}
