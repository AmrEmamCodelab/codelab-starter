import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import { apiErrorFrom } from './data/admin-api-error';
import { ORGANIZATION_KINDS, type Organization, type OrganizationKind, type OrganizationListQuery } from './data/organization.models';
import { OrganizationsAdminClient } from './data/organizations-api';
import { ACTIVE_FILTERS, ActiveFilter, PagedListState, isActiveFor, selectedNumber } from './data/paged-list';
import { EditorMode, OrganizationForm } from './organization-form';

/** An activate or deactivate the admin has asked for and must confirm. */
export interface OrganizationStatusChange {
  readonly organization: Organization;
  /** True reactivates, false deactivates. */
  readonly activate: boolean;
}

/**
 * The state of the organizations screen (provided per page): the list query and its paged rows, the editor dialog
 * (create, edit with the active toggle) and the deactivate / reactivate confirmation. The list reloads whenever the
 * query changes; a filter change goes back to page 1. Every write refreshes the list on success.
 */
@Injectable()
export class OrganizationsPageStore {
  private readonly client = inject(OrganizationsAdminClient);
  private readonly reloads = signal(0);

  readonly search = signal('');
  readonly page = signal(FIRST_PAGE);
  readonly pageSize = DEFAULT_PAGE_SIZE;
  readonly kindFilter = signal<OrganizationKind | null>(null);
  readonly activeFilter = signal(ActiveFilter.All);
  readonly query = computed<OrganizationListQuery>(() => ({
    search: this.search(),
    kind: this.kindFilter(),
    isActive: isActiveFor(this.activeFilter()),
    page: this.page(),
    pageSize: this.pageSize,
  }));
  readonly list = new PagedListState<OrganizationListQuery, Organization>((query) => this.client.list(query));

  private readonly editorForm = signal<OrganizationForm | null>(null);
  private readonly savingNow = signal(false);
  private readonly saveFailure = signal<ApiError | null>(null);
  readonly editor = this.editorForm.asReadonly();
  readonly saving = this.savingNow.asReadonly();
  readonly saveError = this.saveFailure.asReadonly();

  private readonly pendingChange = signal<OrganizationStatusChange | null>(null);
  private readonly statusBusyNow = signal(false);
  private readonly statusFailure = signal<ApiError | null>(null);
  readonly statusChange = this.pendingChange.asReadonly();
  readonly statusBusy = this.statusBusyNow.asReadonly();
  readonly statusError = this.statusFailure.asReadonly();

  constructor() {
    effect(() => {
      const query = this.query();
      this.reloads();
      untracked(() => void this.list.load(query));
    });
  }

  reload(): void {
    this.reloads.update((count) => count + 1);
  }

  /** The kind filter from its `<select>` value (empty = every kind). */
  filterKind(value: string): void {
    this.kindFilter.set(selectedNumber(value, ORGANIZATION_KINDS));
    this.page.set(FIRST_PAGE);
  }

  /** The active-state filter from its `<select>` value (anything unknown = every state). */
  filterActive(value: string): void {
    this.activeFilter.set(selectedNumber(value, ACTIVE_FILTERS) ?? ActiveFilter.All);
    this.page.set(FIRST_PAGE);
  }

  openCreate(): void {
    this.openEditor(OrganizationForm.blank());
  }

  openEdit(organization: Organization): void {
    this.openEditor(OrganizationForm.of(organization));
  }

  closeEditor(): void {
    if (!this.savingNow()) {
      this.editorForm.set(null);
    }
  }

  /** Creates or updates; an edit that changed the active state then deactivates or reactivates. */
  async save(): Promise<void> {
    const form = this.editorForm();
    if (form === null || this.savingNow()) {
      return;
    }
    this.savingNow.set(true);
    this.saveFailure.set(null);
    try {
      await this.write(form);
      this.editorForm.set(null);
      this.reload();
    } catch (error: unknown) {
      this.saveFailure.set(apiErrorFrom(error));
    } finally {
      this.savingNow.set(false);
    }
  }

  askStatusChange(organization: Organization): void {
    this.statusFailure.set(null);
    this.pendingChange.set({ organization, activate: !organization.isActive });
  }

  cancelStatusChange(): void {
    if (!this.statusBusyNow()) {
      this.pendingChange.set(null);
    }
  }

  async confirmStatusChange(): Promise<void> {
    const change = this.pendingChange();
    if (change === null || this.statusBusyNow()) {
      return;
    }
    this.statusBusyNow.set(true);
    this.statusFailure.set(null);
    try {
      await this.setActive(change.organization, change.activate);
      this.pendingChange.set(null);
      this.reload();
    } catch (error: unknown) {
      this.statusFailure.set(apiErrorFrom(error));
    } finally {
      this.statusBusyNow.set(false);
    }
  }

  private openEditor(form: OrganizationForm): void {
    this.saveFailure.set(null);
    this.editorForm.set(form);
  }

  private async write(form: OrganizationForm): Promise<void> {
    if (form.mode === EditorMode.Create || form.id === null) {
      await firstValueFrom(this.client.create(form.toBody()));
      return;
    }
    const updated = await firstValueFrom(this.client.update(form.id, form.toBody()));
    if (form.activeChanged()) {
      await this.setActive(updated, form.isActive());
    }
  }

  private async setActive(organization: Organization, activate: boolean): Promise<void> {
    const call = activate ? this.client.reactivate(organization.id) : this.client.deactivate(organization.id);
    await firstValueFrom(call, { defaultValue: undefined });
  }
}
