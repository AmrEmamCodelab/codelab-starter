import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import type { AdminLookupItem, LookupType } from './data/lookup.models';
import { LookupsAdminClient } from './data/lookups-api';
import { WholeListState } from './data/whole-list';
import { filterLookupItems, pageOf } from './lookup-list';

/** An activate or deactivate the admin has asked for and must confirm. */
export interface LookupStatusChange {
  readonly item: AdminLookupItem;
  /** True activates, false deactivates. */
  readonly activate: boolean;
}

/**
 * The state of one lookup type's values screen (provided per page): the values (active and inactive), searched and
 * paged here, the type's names for the title, and the confirmed activate / deactivate. A confirmed change reloads.
 */
@Injectable()
export class LookupItemsPageStore {
  private readonly client = inject(LookupsAdminClient);
  private readonly typeKey = signal<string | null>(null);

  readonly type = this.typeKey.asReadonly();
  readonly search = signal('');
  readonly page = signal(FIRST_PAGE);
  readonly pageSize = DEFAULT_PAGE_SIZE;
  readonly list = new WholeListState<string, AdminLookupItem>((type) => this.client.items(type));
  /** The types list, read only for the title (the type's bilingual names). */
  readonly types = new WholeListState<void, LookupType>(() => this.client.types());
  readonly typeInfo = computed(() => this.types.items().find((type) => type.type === this.typeKey()) ?? null);
  readonly matches = computed(() => filterLookupItems(this.list.items(), this.search()));
  readonly rows = computed(() => pageOf(this.matches(), this.page(), this.pageSize));
  readonly total = computed(() => this.matches().length);

  private readonly pendingChange = signal<LookupStatusChange | null>(null);
  private readonly statusBusyNow = signal(false);
  private readonly statusFailure = signal<ApiError | null>(null);
  readonly statusChange = this.pendingChange.asReadonly();
  readonly statusBusy = this.statusBusyNow.asReadonly();
  readonly statusError = this.statusFailure.asReadonly();

  /** Opens `type`: loads its values and the type names. */
  async open(type: string): Promise<void> {
    this.typeKey.set(type);
    this.search.set('');
    this.page.set(FIRST_PAGE);
    await Promise.all([this.reload(), this.types.load()]);
  }

  async reload(): Promise<void> {
    const type = this.typeKey();
    if (type !== null) {
      await this.list.load(type);
    }
  }

  askStatusChange(item: AdminLookupItem): void {
    this.statusFailure.set(null);
    this.pendingChange.set({ item, activate: !item.isActive });
  }

  cancelStatusChange(): void {
    if (!this.statusBusyNow()) {
      this.pendingChange.set(null);
    }
  }

  async confirmStatusChange(): Promise<void> {
    const change = this.pendingChange();
    const type = this.typeKey();
    if (change === null || type === null || this.statusBusyNow()) {
      return;
    }
    this.statusBusyNow.set(true);
    this.statusFailure.set(null);
    try {
      await firstValueFrom(this.client.setActive(type, change.item.id, change.activate), { defaultValue: undefined });
      this.pendingChange.set(null);
      await this.reload();
    } catch (error: unknown) {
      this.statusFailure.set(apiErrorFrom(error));
    } finally {
      this.statusBusyNow.set(false);
    }
  }
}
