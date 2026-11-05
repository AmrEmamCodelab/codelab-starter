import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import type { DgaRadioValue } from '@codelab/ng-dga';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom, type Observable } from 'rxjs';
import { LanguageService } from '../../../core/i18n/language.service';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { PermissionStore } from '../../../core/permissions/permission.store';
import { DEFAULT_PAGE_SIZE, FIRST_PAGE } from '../../../patterns';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import { organizationOption, type Organization, type OrganizationId, type OrganizationOption } from '../organizations/data/organization.models';
import { ORGANIZATION_PICKER_PAGE_SIZE, OrganizationsAdminClient } from '../organizations/data/organizations-api';
import { ACTIVE_FILTERS, ActiveFilter, PagedListState, isActiveFor, selectedNumber } from '../organizations/data/paged-list';
import { EditorMode } from '../organizations/organization-form';
import { USER_KINDS, type Activation, type AdminUser, type UserKind, type UserListQuery } from './data/user.models';
import { UsersAdminClient } from './data/users-api';
import { UserForm, organizationChoices } from './user-form';

/** A row action that the admin confirms before it is sent. */
export enum UserAction {
  Deactivate = 1,
  Reactivate = 2,
  Unlock = 3,
  IssueActivation = 4,
}

/** A row action waiting for its confirmation. */
export interface PendingUserAction {
  readonly user: AdminUser;
  readonly action: UserAction;
}

/** An activation link just issued (on create or on request), shown once with the account it belongs to. */
export interface IssuedActivation {
  readonly userName: string;
  readonly activation: Activation;
}

/**
 * The state of the users screen (provided per page): the list query and its paged rows, the organizations for the
 * filter and the editor's picker, the create / edit dialog, the confirmed row actions (deactivate, reactivate, unlock,
 * issue an activation link) and the activation link to hand over. The list reloads whenever the query changes; a
 * filter change goes back to page 1; every successful write refreshes it.
 *
 * Organizations load only for an admin who holds `organizations.view` (the endpoint's key); without it the filter
 * and the picker are hidden and the server still refuses an external user without an organization.
 */
@Injectable()
export class UsersPageStore {
  private readonly client = inject(UsersAdminClient);
  private readonly organizationsClient = inject(OrganizationsAdminClient);
  private readonly permissions = inject(PermissionStore);
  private readonly language = inject(LanguageService);
  private readonly reloads = signal(0);

  readonly search = signal('');
  readonly page = signal(FIRST_PAGE);
  readonly pageSize = DEFAULT_PAGE_SIZE;
  readonly organizationFilter = signal<OrganizationId | null>(null);
  readonly kindFilter = signal<UserKind | null>(null);
  readonly activeFilter = signal(ActiveFilter.All);
  readonly query = computed<UserListQuery>(() => ({
    search: this.search(),
    organizationId: this.organizationFilter(),
    kind: this.kindFilter(),
    isActive: isActiveFor(this.activeFilter()),
    page: this.page(),
    pageSize: this.pageSize,
  }));
  readonly list = new PagedListState<UserListQuery, AdminUser>((query) => this.client.list(query));

  private readonly organizationRows = signal<readonly Organization[]>([]);
  private readonly organizationsFailure = signal<ApiError | null>(null);
  readonly canViewOrganizations = computed(() => this.permissions.has(PermissionKeys.OrganizationsView));
  /** Every organization (the list filter), named in the current language. */
  readonly organizationOptions = computed<readonly OrganizationOption[]>(() =>
    this.organizationRows().map((organization) => organizationOption(organization, this.language.current())));
  readonly organizationsError = this.organizationsFailure.asReadonly();

  private readonly editorForm = signal<UserForm | null>(null);
  private readonly savingNow = signal(false);
  private readonly saveFailure = signal<ApiError | null>(null);
  readonly editor = this.editorForm.asReadonly();
  readonly saving = this.savingNow.asReadonly();
  readonly saveError = this.saveFailure.asReadonly();
  /** The organizations the open editor may pick: active ones of the matching kind, plus the user's own. */
  readonly organizationChoices = computed<readonly OrganizationOption[]>(() => {
    const form = this.editorForm();
    if (form === null) {
      return [];
    }
    const active = new Set(this.organizationRows().filter((row) => row.isActive).map((row) => row.id));
    return organizationChoices(form, this.organizationOptions().filter((option) => active.has(option.id)));
  });

  private readonly pendingAction = signal<PendingUserAction | null>(null);
  private readonly actionBusyNow = signal(false);
  private readonly actionFailure = signal<ApiError | null>(null);
  readonly action = this.pendingAction.asReadonly();
  readonly actionBusy = this.actionBusyNow.asReadonly();
  readonly actionError = this.actionFailure.asReadonly();

  private readonly issuedLink = signal<IssuedActivation | null>(null);
  readonly issued = this.issuedLink.asReadonly();

  constructor() {
    effect(() => {
      const query = this.query();
      this.reloads();
      untracked(() => void this.list.load(query));
    });
    effect(() => {
      if (this.canViewOrganizations()) {
        untracked(() => void this.loadOrganizations());
      }
    });
  }

  reload(): void {
    this.reloads.update((count) => count + 1);
  }

  /** The organization filter from its `<select>` value (empty = every organization). */
  filterOrganization(value: string): void {
    this.organizationFilter.set(this.organizationOptions().find((option) => option.id === value)?.id ?? null);
    this.page.set(FIRST_PAGE);
  }

  filterKind(value: string): void {
    this.kindFilter.set(selectedNumber(value, USER_KINDS));
    this.page.set(FIRST_PAGE);
  }

  filterActive(value: string): void {
    this.activeFilter.set(selectedNumber(value, ACTIVE_FILTERS) ?? ActiveFilter.All);
    this.page.set(FIRST_PAGE);
  }

  openCreate(): void {
    this.openEditor(UserForm.blank());
  }

  openEdit(user: AdminUser): void {
    this.openEditor(UserForm.of(user));
  }

  closeEditor(): void {
    if (!this.savingNow()) {
      this.editorForm.set(null);
    }
  }

  /** A new kind; an organization of the other kind is cleared (the membership rule). */
  setKind(value: DgaRadioValue | null): void {
    const form = this.editorForm();
    if (form === null) {
      return;
    }
    form.kind.set(value);
    const selected = form.organizationId();
    if (selected !== null && !this.organizationChoices().some((option) => option.id === selected)) {
      form.organizationId.set(null);
    }
  }

  /** The picked organization from its `<select>` value (empty = none); only an offered choice is accepted. */
  setOrganization(value: string): void {
    const form = this.editorForm();
    if (form !== null) {
      form.organizationId.set(this.organizationChoices().find((option) => option.id === value)?.id ?? null);
    }
  }

  /** Creates or updates; a create of a password-bound account shows its first activation link. */
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

  ask(user: AdminUser, action: UserAction): void {
    this.actionFailure.set(null);
    this.pendingAction.set({ user, action });
  }

  cancelAction(): void {
    if (!this.actionBusyNow()) {
      this.pendingAction.set(null);
    }
  }

  async confirmAction(): Promise<void> {
    const pending = this.pendingAction();
    if (pending === null || this.actionBusyNow()) {
      return;
    }
    this.actionBusyNow.set(true);
    this.actionFailure.set(null);
    try {
      await this.perform(pending);
      this.pendingAction.set(null);
      this.reload();
    } catch (error: unknown) {
      this.actionFailure.set(apiErrorFrom(error));
    } finally {
      this.actionBusyNow.set(false);
    }
  }

  dismissIssued(): void {
    this.issuedLink.set(null);
  }

  private openEditor(form: UserForm): void {
    this.saveFailure.set(null);
    this.editorForm.set(form);
  }

  private async write(form: UserForm): Promise<void> {
    if (form.mode === EditorMode.Create || form.id === null) {
      const created = await firstValueFrom(this.client.create(form.toCreateBody()));
      if (created.activation !== null) {
        this.issuedLink.set({ userName: created.user.userName, activation: created.activation });
      }
      return;
    }
    await firstValueFrom(this.client.update(form.id, form.toUpdateBody()));
  }

  private async perform({ user, action }: PendingUserAction): Promise<void> {
    if (action === UserAction.IssueActivation) {
      const activation = await firstValueFrom(this.client.issueActivation(user.id));
      this.issuedLink.set({ userName: user.userName, activation });
      return;
    }
    await firstValueFrom(this.statusCall(user, action), { defaultValue: undefined });
  }

  private statusCall(user: AdminUser, action: UserAction): Observable<void> {
    if (action === UserAction.Deactivate) {
      return this.client.deactivate(user.id);
    }
    return action === UserAction.Reactivate ? this.client.reactivate(user.id) : this.client.unlock(user.id);
  }

  private async loadOrganizations(): Promise<void> {
    this.organizationsFailure.set(null);
    try {
      const query = { search: '', kind: null, isActive: null, page: FIRST_PAGE, pageSize: ORGANIZATION_PICKER_PAGE_SIZE };
      const page = await firstValueFrom(this.organizationsClient.list(query));
      this.organizationRows.set(page.items);
    } catch (error: unknown) {
      this.organizationsFailure.set(apiErrorFrom(error));
    }
  }
}
