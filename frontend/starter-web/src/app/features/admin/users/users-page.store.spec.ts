import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { LanguageService } from '../../../core/i18n/language.service';
import { PermissionKeys } from '../../../core/permissions/permission-keys.generated';
import { fakeLanguage } from '../../../layouts/layout.testing';
import { adminUser, grant, organization, problem } from '../organizations/admin.testing';
import { OrganizationKind, type OrganizationId } from '../organizations/data/organization.models';
import { OrganizationsAdminClient } from '../organizations/data/organizations-api';
import { ActiveFilter } from '../organizations/data/paged-list';
import { UserKind } from './data/user.models';
import { UsersAdminClient } from './data/users-api';
import { UserAction, UsersPageStore } from './users-page.store';

const ACTIVATION = { activationLink: 'http://localhost:4280/activate#ticket=abc', expiresAt: '2026-11-06T10:00:00Z' };
const HQ = organization();
const PARTNER = organization({ id: 'org-p' as OrganizationId, code: 'PARTNER', nameEn: 'Partner', kind: OrganizationKind.ExternalOrganization });
const CLOSED = organization({ id: 'org-c' as OrganizationId, code: 'CLOSED', isActive: false });

describe('UsersPageStore', () => {
  let users: Record<string, ReturnType<typeof vi.fn>>;
  let organizations: { list: ReturnType<typeof vi.fn> };
  const language = fakeLanguage(AppLanguages.English);

  async function flush(): Promise<void> {
    for (let round = 0; round < 3; round += 1) {
      TestBed.tick();
      await Promise.resolve();
    }
  }

  function createStore(...keys: Parameters<typeof grant>): UsersPageStore {
    grant(...keys);
    return TestBed.inject(UsersPageStore);
  }

  beforeEach(() => {
    users = {
      list: vi.fn().mockReturnValue(of({ items: [adminUser()], total: '1' })),
      create: vi.fn().mockReturnValue(of({ user: adminUser({ userName: 'new' }), activation: ACTIVATION })),
      update: vi.fn().mockReturnValue(of(adminUser())),
      deactivate: vi.fn().mockReturnValue(of(undefined)),
      reactivate: vi.fn().mockReturnValue(of(undefined)),
      unlock: vi.fn().mockReturnValue(of(undefined)),
      issueActivation: vi.fn().mockReturnValue(of(ACTIVATION)),
    };
    organizations = { list: vi.fn().mockReturnValue(of({ items: [HQ, PARTNER, CLOSED], total: 3 })) };
    TestBed.configureTestingModule({
      providers: [
        UsersPageStore,
        { provide: UsersAdminClient, useValue: users },
        { provide: OrganizationsAdminClient, useValue: organizations },
        { provide: LanguageService, useValue: language },
      ],
    });
  });

  it('loads the first page, and the organizations for a holder of organizations.view', async () => {
    const store = createStore(PermissionKeys.UsersView, PermissionKeys.OrganizationsView);
    await flush();
    expect(users['list']).toHaveBeenCalledWith({ search: '', organizationId: null, kind: null, isActive: null, page: 1, pageSize: 20 });
    expect(store.list.total()).toBe(1);
    expect(organizations.list).toHaveBeenCalledWith(expect.objectContaining({ pageSize: 100, isActive: null }));
    expect(store.organizationOptions().map((option) => option.name)).toEqual(['Head office', 'Partner', 'Head office']);
  });

  it('never asks for organizations without organizations.view', async () => {
    const store = createStore(PermissionKeys.UsersView);
    await flush();
    expect(organizations.list).not.toHaveBeenCalled();
    expect(store.canViewOrganizations()).toBe(false);
  });

  it('keeps a failed organizations load for the editor to mention', async () => {
    organizations.list.mockReturnValue(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
    const store = createStore(PermissionKeys.UsersView, PermissionKeys.OrganizationsView);
    await flush();
    expect(store.organizationsError()?.serverCode).toBe('INTERNAL_ERROR');
  });

  it('filters by organization, kind and status, back on page 1', async () => {
    const store = createStore(PermissionKeys.UsersView, PermissionKeys.OrganizationsView);
    await flush();
    store.page.set(4);
    store.filterOrganization('org-p');
    store.filterKind(String(UserKind.ExternalUser));
    store.filterActive(String(ActiveFilter.Active));
    await flush();
    expect(users['list']).toHaveBeenLastCalledWith({
      search: '',
      organizationId: 'org-p',
      kind: UserKind.ExternalUser,
      isActive: true,
      page: 1,
      pageSize: 20,
    });
    store.filterOrganization('not-listed');
    expect(store.organizationFilter()).toBeNull();
  });

  it('creates a password account and shows its first activation link', async () => {
    const store = createStore(PermissionKeys.UsersView);
    await flush();
    store.openCreate();
    store.editor()?.userName.set('new');
    await store.save();
    expect(users['create']).toHaveBeenCalledWith(expect.objectContaining({ userName: 'new' }));
    expect(store.editor()).toBeNull();
    expect(store.issued()).toEqual({ userName: 'new', activation: ACTIVATION });
    store.dismissIssued();
    expect(store.issued()).toBeNull();
  });

  it('shows no link for a directory account', async () => {
    users['create'].mockReturnValue(of({ user: adminUser(), activation: null }));
    const store = createStore(PermissionKeys.UsersView);
    store.openCreate();
    await store.save();
    expect(store.issued()).toBeNull();
  });

  it('keeps the editor open with the server refusal', async () => {
    users['update'].mockReturnValue(throwError(() => problem(HttpStatus.conflict, 'IDENTITY_EMAIL_TAKEN')));
    const store = createStore(PermissionKeys.UsersView);
    store.openEdit(adminUser());
    await store.save();
    expect(users['update']).toHaveBeenCalledWith('user-1', expect.not.objectContaining({ userName: expect.anything() }));
    expect(store.editor()).not.toBeNull();
    expect(store.saveError()?.serverCode).toBe('IDENTITY_EMAIL_TAKEN');
    store.closeEditor();
    expect(store.editor()).toBeNull();
  });

  it('offers only active organizations of the matching kind, and clears a mismatched one on a kind change', async () => {
    const store = createStore(PermissionKeys.UsersView, PermissionKeys.OrganizationsView);
    await flush();
    store.openCreate();
    expect(store.organizationChoices().map((choice) => choice.id)).toEqual(['org-1']);
    store.setOrganization('org-1');
    expect(store.editor()?.organizationId()).toBe('org-1');
    store.setKind(UserKind.ExternalUser);
    expect(store.editor()?.organizationId()).toBeNull();
    expect(store.organizationChoices().map((choice) => choice.id)).toEqual(['org-p']);
    store.setOrganization('org-c');
    expect(store.editor()?.organizationId()).toBeNull();
  });

  it.each([
    [UserAction.Deactivate, 'deactivate'],
    [UserAction.Reactivate, 'reactivate'],
    [UserAction.Unlock, 'unlock'],
  ] as const)('confirms action %s before calling %s, then reloads', async (action, method) => {
    const store = createStore(PermissionKeys.UsersView);
    await flush();
    const calls = users['list'].mock.calls.length;
    store.ask(adminUser(), action);
    expect(users[method]).not.toHaveBeenCalled();
    await store.confirmAction();
    expect(users[method]).toHaveBeenCalledWith('user-1');
    expect(store.action()).toBeNull();
    await flush();
    expect(users['list'].mock.calls.length).toBe(calls + 1);
  });

  it('issues a new activation link after confirmation', async () => {
    const store = createStore(PermissionKeys.UsersView);
    store.ask(adminUser(), UserAction.IssueActivation);
    await store.confirmAction();
    expect(store.issued()).toEqual({ userName: 'sara', activation: ACTIVATION });
  });

  it('keeps a refused action open with its error, and cancels it', async () => {
    users['deactivate'].mockReturnValue(throwError(() => problem(HttpStatus.conflict, 'USERS_CANNOT_DEACTIVATE_SELF')));
    const store = createStore(PermissionKeys.UsersView);
    store.ask(adminUser(), UserAction.Deactivate);
    await store.confirmAction();
    expect(store.actionError()?.serverCode).toBe('USERS_CANNOT_DEACTIVATE_SELF');
    expect(store.action()).not.toBeNull();
    store.cancelAction();
    expect(store.action()).toBeNull();
  });
});
