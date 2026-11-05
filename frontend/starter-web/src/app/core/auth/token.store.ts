import { DOCUMENT, DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import type { SessionResponse, SessionUser } from '../api/clients/api.models';
import { AppStorage } from '../session/app-storage';
import { StorageKeys } from '../session/storage-keys';
import { SignInMode, parseSignInMode } from './sign-in-mode';

/** The session the browser holds between page loads. */
export interface StoredSession {
  readonly accessToken: string;
  readonly refreshToken: string;
  /** Epoch milliseconds the access token stops being accepted. */
  readonly accessTokenExpiresAt: number;
  readonly idleTimeoutMinutes: number;
  readonly user: SessionUser;
  /** How the session was started; decides how logout ends it. */
  readonly signInMode: SignInMode;
}

/** The `storage` DOM event name. */
const STORAGE_EVENT = 'storage';

/**
 * Holds the session tokens, in memory as a signal and in browser storage (so a reload and other tabs
 * keep the session). Dependency-free apart from storage, so `ApiService` can read the token without
 * depending on `AuthService`. Only `AuthService` writes it.
 *
 * Tabs share one session. The refresh token is single-use (the server ends the session when a rotated
 * secret is sent again), so a tab adopts the tokens another tab rotated: on the `storage` event of
 * {@link StorageKeys.AccessTokenExpiresAt}, written last by {@link save} and removed last by {@link clear}
 * so a tab never reads half a session, and on {@link adoptStored}, which `AuthService` calls before it
 * refreshes.
 */
@Injectable({ providedIn: 'root' })
export class TokenStore {
  private readonly storage = inject(AppStorage);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly current = signal<StoredSession | null>(this.restore());
  private readonly onStorage = (event: StorageEvent): void => {
    if (event.key !== StorageKeys.AccessTokenExpiresAt && event.key !== null) {
      return;
    }
    if (event.newValue === null && this.restore() === null) {
      // Another tab signed out (or its session ended): the server session is gone for this tab too.
      this.current.set(null);
      return;
    }
    this.adoptStored();
  };

  constructor() {
    this.window?.addEventListener(STORAGE_EVENT, this.onStorage);
    inject(DestroyRef).onDestroy(() => this.window?.removeEventListener(STORAGE_EVENT, this.onStorage));
  }

  /** The session, or null when signed out. */
  readonly session = this.current.asReadonly();
  readonly user = computed(() => this.current()?.user ?? null);
  /** How the current session was started, or null when signed out. */
  readonly signInMode = computed(() => this.current()?.signInMode ?? null);

  accessToken(): string | null {
    return this.current()?.accessToken ?? null;
  }

  refreshToken(): string | null {
    return this.current()?.refreshToken ?? null;
  }

  /**
   * Stores a login or refresh response.
   *
   * @param signInMode how the session was started; a refresh passes the mode of the session it rotates.
   */
  save(response: SessionResponse, signInMode: SignInMode): StoredSession {
    const session: StoredSession = {
      accessToken: response.accessToken,
      refreshToken: response.refreshToken,
      accessTokenExpiresAt: Date.parse(response.accessTokenExpiresAt),
      idleTimeoutMinutes: response.idleTimeoutMinutes,
      user: response.user,
      signInMode,
    };
    this.storage.write(StorageKeys.AccessToken, session.accessToken);
    this.storage.write(StorageKeys.RefreshToken, session.refreshToken);
    this.storage.write(StorageKeys.IdleTimeoutMinutes, String(session.idleTimeoutMinutes));
    this.storage.write(StorageKeys.SessionUser, JSON.stringify(session.user));
    this.storage.write(StorageKeys.SignInMode, String(session.signInMode));
    // Last: other tabs adopt the session on this key's storage event, so every other part is in place.
    this.storage.write(StorageKeys.AccessTokenExpiresAt, String(session.accessTokenExpiresAt));
    this.current.set(session);
    return session;
  }

  clear(): void {
    this.storage.remove(StorageKeys.AccessToken);
    this.storage.remove(StorageKeys.RefreshToken);
    this.storage.remove(StorageKeys.IdleTimeoutMinutes);
    this.storage.remove(StorageKeys.SessionUser);
    this.storage.remove(StorageKeys.SignInMode);
    this.storage.remove(StorageKeys.AccessTokenExpiresAt);
    this.current.set(null);
  }

  /**
   * Adopts a complete stored session whose refresh token differs from the one in memory (another tab
   * rotated or signed in). A missing or partial stored session changes nothing, so a storage that
   * refuses writes never signs this tab out.
   *
   * @returns the session now in memory.
   */
  adoptStored(): StoredSession | null {
    const stored = this.restore();
    if (stored !== null && stored.refreshToken !== this.current()?.refreshToken) {
      this.current.set(stored);
    }
    return this.current();
  }

  /** A stored session, or null when any part is missing or unreadable (a partial session is no session). */
  private restore(): StoredSession | null {
    const accessToken = this.storage.read(StorageKeys.AccessToken);
    const refreshToken = this.storage.read(StorageKeys.RefreshToken);
    const accessTokenExpiresAt = Number(this.storage.read(StorageKeys.AccessTokenExpiresAt));
    const idleTimeoutMinutes = Number(this.storage.read(StorageKeys.IdleTimeoutMinutes));
    const user = parseUser(this.storage.read(StorageKeys.SessionUser));
    if (!accessToken || !refreshToken || !user || !Number.isFinite(accessTokenExpiresAt)) {
      return null;
    }
    if (!Number.isFinite(idleTimeoutMinutes)) {
      return null;
    }
    const signInMode = parseSignInMode(this.storage.read(StorageKeys.SignInMode));
    return { accessToken, refreshToken, accessTokenExpiresAt, idleTimeoutMinutes, user, signInMode };
  }
}

function parseUser(raw: string | null): SessionUser | null {
  if (!raw) {
    return null;
  }
  try {
    const value = JSON.parse(raw) as Partial<SessionUser> | null;
    return typeof value?.id === 'string' && typeof value.displayName === 'string'
      ? { id: value.id, displayName: value.displayName }
      : null;
  } catch {
    return null;
  }
}
