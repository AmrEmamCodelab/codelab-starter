import { Injectable, computed, signal } from '@angular/core';

/**
 * The permission keys the signed-in user holds, as the server resolved them on the last sync
 * (`GET api/me/permissions`). The token carries no permission claims, so this store is the browser's
 * only view of them. It gates routes and buttons; the server still authorizes every call.
 *
 * Keys are typed as strings here; screens pass `PermissionKeys.*` from the generated
 * `permission-keys.generated.ts`.
 *
 * Everything here is a signal: `has()` read inside a template, a `computed` or the `*hasPermission`
 * directive re-evaluates when a background sync changes the keys, so menus and buttons follow a grant or
 * revoke without a reload. A sync that returns the same keys writes nothing, so nothing re-renders.
 */
@Injectable({ providedIn: 'root' })
export class PermissionStore {
  private readonly keys = signal<ReadonlySet<string>>(new Set(), { equal: sameKeys });
  private readonly synced = signal(false);

  /** The held keys as a set (a new set only when the keys changed). */
  readonly held = this.keys.asReadonly();
  /** The held keys, sorted. */
  readonly permissions = computed(() => [...this.keys()].sort());
  /** True once a sync has succeeded for the current session. */
  readonly loaded = this.synced.asReadonly();

  has(key: string): boolean {
    return this.keys().has(key);
  }

  set(keys: readonly string[]): void {
    this.keys.set(new Set(keys));
    this.synced.set(true);
  }

  clear(): void {
    this.keys.set(new Set());
    this.synced.set(false);
  }
}

function sameKeys(left: ReadonlySet<string>, right: ReadonlySet<string>): boolean {
  return left.size === right.size && [...left].every(key => right.has(key));
}
