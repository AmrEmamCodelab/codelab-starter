import { Injectable, computed, inject, signal } from '@angular/core';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import type { LookupType } from './data/lookup.models';
import { LookupsAdminClient } from './data/lookups-api';
import { WholeListState } from './data/whole-list';
import { filterLookupTypes, pageOf } from './lookup-list';

/**
 * The state of the lookup types screen (provided per page): every type the server lists, searched and paged here.
 * Read-only: types and their values are created and renamed only by migrations (`/new-lookup`).
 */
@Injectable()
export class LookupTypesPageStore {
  private readonly client = inject(LookupsAdminClient);

  readonly search = signal('');
  readonly page = signal(FIRST_PAGE);
  readonly pageSize = DEFAULT_PAGE_SIZE;
  readonly list = new WholeListState<void, LookupType>(() => this.client.types());
  readonly matches = computed(() => filterLookupTypes(this.list.items(), this.search()));
  readonly rows = computed(() => pageOf(this.matches(), this.page(), this.pageSize));
  readonly total = computed(() => this.matches().length);

  load(): Promise<void> {
    return this.list.load();
  }
}
