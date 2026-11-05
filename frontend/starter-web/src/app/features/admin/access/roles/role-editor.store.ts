import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { PermissionSyncService } from '../../../../core/permissions/permission-sync.service';
import { apiErrorFrom, isLastManagerRefusal } from '../common/access-errors';
import { AccessAdminClient } from '../data/access-api';
import type { CatalogPermission, RoleDetail, RoleId } from '../data/access.models';
import { draftFromRole, heldRetiredKeys, isRoleDraftDirty, roleUpdateBody, splitCatalog } from './role-draft';

/**
 * The state of the role editor: the catalog, the saved role and the draft (names and selected keys), dirty
 * tracking, and the save with its last-manager confirmation. Provided by the page.
 */
@Injectable()
export class RoleEditorStore {
  private readonly client = inject(AccessAdminClient);
  private readonly permissionSync = inject(PermissionSyncService);

  readonly catalog = signal<readonly CatalogPermission[]>([]);
  readonly role = signal<RoleDetail | null>(null);
  readonly nameEn = signal('');
  readonly nameAr = signal('');
  readonly selection = signal<ReadonlySet<string>>(new Set());

  readonly loading = signal(false);
  readonly loadError = signal<ApiError | null>(null);
  readonly saving = signal(false);
  readonly saveError = signal<ApiError | null>(null);
  /** True after a save until the next edit (the page shows "saved" while the draft is clean). */
  readonly saved = signal(false);
  readonly warnings = signal<readonly string[]>([]);
  /** A save refused with ACCESS_LAST_MANAGER, waiting for the user's confirmation. */
  readonly lastManagerPending = signal(false);

  readonly split = computed(() => splitCatalog(this.catalog()));
  readonly catalogByKey = computed(() => new Map(this.catalog().map((entry) => [entry.key, entry])));
  readonly retiredHeld = computed(() => {
    const role = this.role();
    return role ? heldRetiredKeys(role, this.split().liveSet) : [];
  });
  readonly draft = computed(() => ({ nameEn: this.nameEn(), nameAr: this.nameAr(), permissions: this.selection() }));
  readonly dirty = computed(() => {
    const role = this.role();
    return role !== null && isRoleDraftDirty(role, this.draft(), this.split().liveSet);
  });

  async load(id: RoleId): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      const [catalog, role] = await Promise.all([
        firstValueFrom(this.client.permissions()),
        firstValueFrom(this.client.role(id)),
      ]);
      this.catalog.set(catalog);
      this.apply(role);
    } catch (error) {
      this.loadError.set(apiErrorFrom(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Puts the draft back to the saved role. */
  reset(): void {
    const role = this.role();
    if (role) {
      this.apply(role);
    }
  }

  /**
   * Saves the draft; resolves true on success. A last-manager refusal without `confirmBreakGlassOnly` sets
   * {@link lastManagerPending}; any other refusal lands in {@link saveError}.
   */
  async save(confirmBreakGlassOnly = false): Promise<boolean> {
    const role = this.role();
    if (!role) {
      return false;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.warnings.set([]);
    try {
      const body = roleUpdateBody(role, this.draft(), this.split().liveSet, confirmBreakGlassOnly);
      const saved = await firstValueFrom(this.client.updateRole(role.id, body));
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

  private apply(role: RoleDetail): void {
    const draft = draftFromRole(role, this.split().liveSet);
    this.role.set(role);
    this.nameEn.set(draft.nameEn);
    this.nameAr.set(draft.nameAr);
    this.selection.set(draft.permissions);
    this.saved.set(false);
  }
}
