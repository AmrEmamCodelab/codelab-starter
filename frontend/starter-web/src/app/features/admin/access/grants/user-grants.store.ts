import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom, of } from 'rxjs';
import { PermissionKeys } from '../../../../core/permissions/permission-keys.generated';
import { PermissionSyncService } from '../../../../core/permissions/permission-sync.service';
import { apiErrorFrom, isLastManagerRefusal } from '../common/access-errors';
import { AccessAdminClient } from '../data/access-api';
import type {
  CatalogPermission,
  PermissionScope,
  RoleId,
  RoleSummary,
  UserGrants,
} from '../data/access.models';
import {
  directPermissionKeys,
  draftFromGrants,
  effectiveRows,
  isGrantsDraftDirty,
  putGrantsBody,
  roleOptions,
  withDirectSelection,
  withRole,
  withScope,
} from './grants-draft';

/** The keys this client was built with: the direct-grant tree's fallback when the viewer cannot read the catalog. */
const BUILT_IN_PERMISSION_KEYS: readonly string[] = Object.values(PermissionKeys);

/** What the page may load besides the grants (both need `access.roles.view`). */
export interface GrantsLoadOptions {
  readonly withRolesAndCatalog: boolean;
}

/**
 * The state of the user grant editor: the user's grants, the role options, the direct-permission keys, the draft
 * (roles and direct permissions with scopes), dirty tracking and the save with its last-manager confirmation.
 * Provided by the page.
 */
@Injectable()
export class UserGrantsStore {
  private readonly client = inject(AccessAdminClient);
  private readonly permissionSync = inject(PermissionSyncService);

  private userId: string | null = null;

  readonly grants = signal<UserGrants | null>(null);
  readonly allRoles = signal<readonly RoleSummary[] | null>(null);
  readonly catalog = signal<readonly CatalogPermission[] | null>(null);
  readonly roleDraft = signal<ReadonlyMap<RoleId, PermissionScope>>(new Map());
  readonly directDraft = signal<ReadonlyMap<string, PermissionScope>>(new Map());

  readonly loading = signal(false);
  readonly loadError = signal<ApiError | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<ApiError | null>(null);
  readonly saved = signal(false);
  readonly warnings = signal<readonly string[]>([]);
  readonly lastManagerPending = signal(false);

  readonly catalogByKey = computed(() => new Map((this.catalog() ?? []).map((entry) => [entry.key, entry])));
  readonly roleOptions = computed(() => {
    const grants = this.grants();
    return grants ? roleOptions(this.allRoles(), grants) : [];
  });
  readonly directKeys = computed(() => {
    const grants = this.grants();
    const catalog = this.catalog();
    const live = catalog === null ? null : catalog.filter((entry) => !entry.isRetired).map((entry) => entry.key);
    return grants ? directPermissionKeys(live, BUILT_IN_PERMISSION_KEYS, grants) : [];
  });
  readonly directSelection = computed<ReadonlySet<string>>(() => new Set(this.directDraft().keys()));
  readonly effective = computed(() => {
    const grants = this.grants();
    return grants ? effectiveRows(grants) : [];
  });
  readonly dirty = computed(() => {
    const grants = this.grants();
    return grants !== null && isGrantsDraftDirty(grants, { roles: this.roleDraft(), permissions: this.directDraft() });
  });

  async load(userId: string, options: GrantsLoadOptions): Promise<void> {
    this.userId = userId;
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const [grants, roles, catalog] = await Promise.all([
        firstValueFrom(this.client.userGrants(userId)),
        firstValueFrom(options.withRolesAndCatalog ? this.client.roles() : of(null)),
        firstValueFrom(options.withRolesAndCatalog ? this.client.permissions() : of(null)),
      ]);
      this.allRoles.set(roles);
      this.catalog.set(catalog);
      this.apply(grants);
    } catch (error) {
      this.loadError.set(apiErrorFrom(error));
    } finally {
      this.loading.set(false);
    }
  }

  setRoleAssigned(roleId: RoleId, assigned: boolean): void {
    this.roleDraft.update((roles) => withRole(roles, roleId, assigned));
  }

  setRoleScope(roleId: RoleId, scope: PermissionScope): void {
    this.roleDraft.update((roles) => withScope(roles, roleId, scope));
  }

  setDirectSelection(keys: ReadonlySet<string>): void {
    this.directDraft.update((permissions) => withDirectSelection(permissions, keys));
  }

  setDirectScope(key: string, scope: PermissionScope): void {
    this.directDraft.update((permissions) => withScope(permissions, key, scope));
  }

  reset(): void {
    const grants = this.grants();
    if (grants) {
      this.apply(grants);
    }
  }

  /**
   * Saves the draft; resolves true on success. A last-manager refusal without `confirmBreakGlassOnly` sets
   * {@link lastManagerPending}; any other refusal lands in {@link saveError}.
   */
  async save(confirmBreakGlassOnly = false): Promise<boolean> {
    if (this.userId === null || this.grants() === null) {
      return false;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.warnings.set([]);
    try {
      const body = putGrantsBody({ roles: this.roleDraft(), permissions: this.directDraft() }, confirmBreakGlassOnly);
      const saved = await firstValueFrom(this.client.putUserGrants(this.userId, body));
      this.lastManagerPending.set(false);
      this.apply(saved);
      this.saved.set(true);
      this.warnings.set(saved.warnings);
      this.permissionSync.refresh().catch(() => undefined);
      return true;
    } catch (error) {
      const refusal = apiErrorFrom(error);
      const askToConfirm = isLastManagerRefusal(refusal) && !confirmBreakGlassOnly;
      this.lastManagerPending.set(askToConfirm);
      this.saveError.set(askToConfirm ? null : refusal);
      return false;
    } finally {
      this.saving.set(false);
    }
  }

  confirmLastManager(): Promise<boolean> {
    return this.save(true);
  }

  cancelLastManager(): void {
    this.lastManagerPending.set(false);
  }

  private apply(grants: UserGrants): void {
    const draft = draftFromGrants(grants);
    this.grants.set(grants);
    this.roleDraft.set(draft.roles);
    this.directDraft.set(draft.permissions);
    this.saved.set(false);
  }
}
