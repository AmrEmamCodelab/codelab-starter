import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import { AdminSettingsClient } from './admin-settings.client';
import { SettingDraft } from './setting-value';
import type { RuntimeSetting } from './setting.models';

/** The group filter's "every group" value (the empty `<select>` option). */
export const ALL_GROUPS = '';

/** Orders settings by group, then key (ordinal, so the order is the same in every language). */
export function compareSettings(left: RuntimeSetting, right: RuntimeSetting): number {
  if (left.group !== right.group) {
    return left.group < right.group ? -1 : 1;
  }
  if (left.key === right.key) {
    return 0;
  }
  return left.key < right.key ? -1 : 1;
}

/** Whether `setting` matches the (trimmed, case-insensitive) search text: key, group or either description. */
export function settingMatches(setting: RuntimeSetting, search: string): boolean {
  const needle = search.trim().toLowerCase();
  if (needle === '') {
    return true;
  }
  return [setting.key, setting.group, setting.descriptionAr, setting.descriptionEn].some((text) =>
    text.toLowerCase().includes(needle));
}

/**
 * The state of the runtime settings screen (provided per page). The API answers every setting at once, so search,
 * the group filter and paging run here: the rows are grouped (ordered by group, then key), and a new search or filter
 * goes back to page 1. The editor dialog edits one value with a type-aware draft; a successful save replaces that row
 * with the server's answer.
 */
@Injectable()
export class SettingsPageStore {
  private readonly client = inject(AdminSettingsClient);

  private readonly settings = signal<readonly RuntimeSetting[]>([]);
  private readonly busy = signal(false);
  private readonly failure = signal<ApiError | null>(null);
  private sequence = 0;

  readonly loading = this.busy.asReadonly();
  readonly error = this.failure.asReadonly();
  readonly search = signal('');
  readonly page = signal(FIRST_PAGE);
  readonly pageSize = DEFAULT_PAGE_SIZE;
  readonly groupFilter = signal(ALL_GROUPS);

  /** The groups present, in order, for the filter. */
  readonly groups = computed(() => [...new Set(this.settings().map((setting) => setting.group))].sort());
  readonly filtered = computed(() => {
    const group = this.groupFilter();
    const search = this.search();
    return this.settings()
      .filter((setting) => (group === ALL_GROUPS || setting.group === group) && settingMatches(setting, search))
      .sort(compareSettings);
  });
  readonly total = computed(() => this.filtered().length);
  /** The rows of the current page. */
  readonly rows = computed(() => {
    const start = (this.page() - FIRST_PAGE) * this.pageSize;
    return this.filtered().slice(start, start + this.pageSize);
  });

  private readonly draftNow = signal<SettingDraft | null>(null);
  private readonly savingNow = signal(false);
  private readonly saveFailure = signal<ApiError | null>(null);
  readonly draft = this.draftNow.asReadonly();
  readonly saving = this.savingNow.asReadonly();
  readonly saveError = this.saveFailure.asReadonly();
  /** Confirm stays disabled while the draft fails the client check or changes nothing. */
  readonly cannotSave = computed(() => {
    const draft = this.draftNow();
    return draft === null || draft.problem() !== null || !draft.changed();
  });

  /** Loads every setting; only the latest request's answer is kept, and a failure clears the rows. */
  async load(): Promise<void> {
    this.sequence += 1;
    const current = this.sequence;
    this.busy.set(true);
    this.failure.set(null);
    try {
      const settings = await firstValueFrom(this.client.list());
      if (current === this.sequence) {
        this.settings.set(settings);
      }
    } catch (error: unknown) {
      if (current === this.sequence) {
        this.settings.set([]);
        this.failure.set(apiErrorFrom(error));
      }
    } finally {
      if (current === this.sequence) {
        this.busy.set(false);
      }
    }
  }

  /** The group filter from its `<select>` value (empty = every group). */
  filterGroup(group: string): void {
    this.groupFilter.set(group);
    this.page.set(FIRST_PAGE);
  }

  openEdit(setting: RuntimeSetting): void {
    this.saveFailure.set(null);
    this.draftNow.set(new SettingDraft(setting));
  }

  closeEditor(): void {
    if (!this.savingNow()) {
      this.draftNow.set(null);
    }
  }

  /** Saves the draft when it passes the client check and changes the value; the row then shows the server's answer. */
  async save(): Promise<void> {
    const draft = this.draftNow();
    if (draft === null || this.savingNow() || this.cannotSave()) {
      return;
    }
    this.savingNow.set(true);
    this.saveFailure.set(null);
    try {
      const updated = await firstValueFrom(this.client.update(draft.setting.key, { value: draft.value() }));
      this.settings.update((settings) => settings.map((setting) => (setting.key === updated.key ? updated : setting)));
      this.draftNow.set(null);
    } catch (error: unknown) {
      this.saveFailure.set(apiErrorFrom(error));
    } finally {
      this.savingNow.set(false);
    }
  }
}
