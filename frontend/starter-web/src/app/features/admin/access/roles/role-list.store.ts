import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { PermissionSyncService } from '../../../../core/permissions/permission-sync.service';
import { FIRST_PAGE } from '../../../../patterns';
import { apiErrorFrom, isLastManagerRefusal } from '../common/access-errors';
import { AccessAdminClient } from '../data/access-api';
import type { RoleDetail, RoleSummary } from '../data/access.models';
import { ROLE_LIST_PAGE_SIZE, activationBody, createRoleBody, filterRoles, pageOf, type NewRoleDraft } from './role-list';

/** A deactivation or reactivation waiting for the last-manager confirmation. */
export interface PendingActivation {
  readonly role: RoleSummary;
  readonly isActive: boolean;
}

/**
 * The state of the roles list: the roles, the search and page, the create dialog and the active toggle. Provided
 * by the page. A write refreshes the signed-in user's permissions, so a change to a role they hold shows at once.
 */
@Injectable()
export class RoleListStore {
  private readonly client = inject(AccessAdminClient);
  private readonly permissionSync = inject(PermissionSyncService);

  readonly roles = signal<readonly RoleSummary[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal<ApiError | null>(null);
  readonly search = signal('');
  readonly page = signal(FIRST_PAGE);
  readonly pageSize = ROLE_LIST_PAGE_SIZE;

  readonly filtered = computed(() => filterRoles(this.roles(), this.search()));
  readonly total = computed(() => this.filtered().length);
  readonly rows = computed(() => pageOf(this.filtered(), this.page(), this.pageSize));

  readonly creating = signal(false);
  readonly createError = signal<ApiError | null>(null);

  readonly busyRoleId = signal<string | null>(null);
  readonly actionError = signal<ApiError | null>(null);
  readonly pendingActivation = signal<PendingActivation | null>(null);
  /** Warning codes of the last successful write (`ACCESS_ONLY_BREAK_GLASS_MANAGES`). */
  readonly warnings = signal<readonly string[]>([]);

  async load(): Promise<void> {
    this.loading.set(true);
    this.loadError.set(null);
    try {
      this.roles.set(await firstValueFrom(this.client.roles()));
    } catch (error) {
      this.loadError.set(apiErrorFrom(error));
    } finally {
      this.loading.set(false);
    }
  }

  /** Creates the role; resolves with it, or with null after setting {@link createError}. */
  async create(draft: NewRoleDraft): Promise<RoleDetail | null> {
    this.creating.set(true);
    this.createError.set(null);
    try {
      return await firstValueFrom(this.client.createRole(createRoleBody(draft)));
    } catch (error) {
      this.createError.set(apiErrorFrom(error));
      return null;
    } finally {
      this.creating.set(false);
    }
  }

  clearCreateError(): void {
    this.createError.set(null);
  }

  /**
   * Deactivates or reactivates the role. A last-manager refusal without `confirmBreakGlassOnly` becomes
   * {@link pendingActivation} so the page can ask; any other refusal lands in {@link actionError}.
   */
  async setActive(role: RoleSummary, isActive: boolean, confirmBreakGlassOnly = false): Promise<void> {
    this.busyRoleId.set(role.id);
    this.actionError.set(null);
    this.warnings.set([]);
    try {
      const detail = await firstValueFrom(this.client.role(role.id));
      const saved = await firstValueFrom(
        this.client.updateRole(role.id, activationBody(detail, isActive, confirmBreakGlassOnly)));
      this.pendingActivation.set(null);
      this.warnings.set(saved.warnings);
      this.replaceRow(saved);
      this.permissionSync.refresh().catch(() => undefined);
    } catch (error) {
      const refusal = apiErrorFrom(error);
      if (isLastManagerRefusal(refusal) && !confirmBreakGlassOnly) {
        this.pendingActivation.set({ role, isActive });
      } else {
        this.pendingActivation.set(null);
        this.actionError.set(refusal);
      }
    } finally {
      this.busyRoleId.set(null);
    }
  }

  /** Resends the pending change, accepting that only the break-glass administrator will manage access. */
  async confirmPending(): Promise<void> {
    const pending = this.pendingActivation();
    if (pending) {
      await this.setActive(pending.role, pending.isActive, true);
    }
  }

  cancelPending(): void {
    this.pendingActivation.set(null);
  }

  private replaceRow(saved: RoleDetail): void {
    this.roles.update((rows) => rows.map((row) => (row.id === saved.id
      ? { ...row, nameEn: saved.nameEn, nameAr: saved.nameAr, isActive: saved.isActive, permissionCount: saved.permissions.length }
      : row)));
  }
}
