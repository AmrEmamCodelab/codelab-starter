import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { organization, problem } from './admin.testing';
import { OrganizationKind, type Organization } from './data/organization.models';
import { OrganizationsAdminClient } from './data/organizations-api';
import { ActiveFilter } from './data/paged-list';
import { OrganizationsPageStore } from './organizations-page.store';

describe('OrganizationsPageStore', () => {
  const rows: readonly Organization[] = [organization()];
  let client: {
    list: ReturnType<typeof vi.fn>;
    create: ReturnType<typeof vi.fn>;
    update: ReturnType<typeof vi.fn>;
    deactivate: ReturnType<typeof vi.fn>;
    reactivate: ReturnType<typeof vi.fn>;
  };
  let store: OrganizationsPageStore;

  async function flush(): Promise<void> {
    TestBed.tick();
    await Promise.resolve();
    await Promise.resolve();
  }

  beforeEach(async () => {
    client = {
      list: vi.fn().mockReturnValue(of({ items: rows, total: 1 })),
      create: vi.fn().mockReturnValue(of(organization())),
      update: vi.fn().mockImplementation((_id: string, body: object) => of({ ...organization(), ...body })),
      deactivate: vi.fn().mockReturnValue(of(undefined)),
      reactivate: vi.fn().mockReturnValue(of(undefined)),
    };
    TestBed.configureTestingModule({
      providers: [OrganizationsPageStore, { provide: OrganizationsAdminClient, useValue: client }],
    });
    store = TestBed.inject(OrganizationsPageStore);
    await flush();
  });

  it('loads the first page on start', () => {
    expect(client.list).toHaveBeenCalledWith({ search: '', kind: null, isActive: null, page: 1, pageSize: 20 });
    expect(store.list.items()).toEqual(rows);
    expect(store.list.total()).toBe(1);
  });

  it('reloads on a new filter and goes back to page 1', async () => {
    store.page.set(3);
    store.filterKind(String(OrganizationKind.ExternalOrganization));
    store.filterActive(String(ActiveFilter.Inactive));
    await flush();
    expect(client.list).toHaveBeenLastCalledWith({ search: '', kind: OrganizationKind.ExternalOrganization, isActive: false, page: 1, pageSize: 20 });
  });

  it('treats unknown filter values as "every"', () => {
    store.filterKind('');
    store.filterActive('99');
    expect(store.kindFilter()).toBeNull();
    expect(store.activeFilter()).toBe(ActiveFilter.All);
  });

  it('reloads the same query on retry', async () => {
    const calls = client.list.mock.calls.length;
    store.reload();
    await flush();
    expect(client.list.mock.calls.length).toBe(calls + 1);
  });

  it('creates an organization, closes the editor and reloads', async () => {
    store.openCreate();
    const form = store.editor();
    form?.code.set('NEW');
    form?.kind.set(OrganizationKind.ExternalOrganization);
    await store.save();
    expect(client.create).toHaveBeenCalledWith({ code: 'NEW', nameEn: '', nameAr: '', kind: OrganizationKind.ExternalOrganization });
    expect(store.editor()).toBeNull();
    await flush();
    expect(client.list).toHaveBeenCalledTimes(2);
  });

  it('keeps the editor open with the refusal when a save fails', async () => {
    client.create.mockReturnValue(throwError(() => problem(HttpStatus.badRequest, 'VALIDATION_FAILED', { code: ['REQUIRED'] })));
    store.openCreate();
    await store.save();
    expect(store.editor()).not.toBeNull();
    expect(store.saveError()?.fieldErrors).toEqual([{ field: 'code', message: 'REQUIRED' }]);
    expect(store.saving()).toBe(false);
  });

  it('updates an edit, then applies a changed active state', async () => {
    store.openEdit(organization());
    store.editor()?.isActive.set(false);
    await store.save();
    expect(client.update).toHaveBeenCalledWith('org-1', expect.objectContaining({ code: 'HQ' }));
    expect(client.deactivate).toHaveBeenCalledWith('org-1');
    expect(client.reactivate).not.toHaveBeenCalled();
  });

  it('does not touch the active state when it did not change', async () => {
    store.openEdit(organization());
    await store.save();
    expect(client.deactivate).not.toHaveBeenCalled();
    expect(client.reactivate).not.toHaveBeenCalled();
  });

  it('confirms a deactivation before sending it', async () => {
    store.askStatusChange(organization());
    expect(store.statusChange()).toEqual({ organization: organization(), activate: false });
    expect(client.deactivate).not.toHaveBeenCalled();
    await store.confirmStatusChange();
    expect(client.deactivate).toHaveBeenCalledWith('org-1');
    expect(store.statusChange()).toBeNull();
  });

  it('reactivates a deactivated organization and shows a refusal inline', async () => {
    client.reactivate.mockReturnValue(throwError(() => problem(HttpStatus.notFound, 'USERS_ORGANIZATION_NOT_FOUND')));
    store.askStatusChange(organization({ isActive: false }));
    await store.confirmStatusChange();
    expect(client.reactivate).toHaveBeenCalled();
    expect(store.statusChange()).not.toBeNull();
    expect(store.statusError()?.serverCode).toBe('USERS_ORGANIZATION_NOT_FOUND');
    store.cancelStatusChange();
    expect(store.statusChange()).toBeNull();
  });

  it('closes the editor on cancel', () => {
    store.openCreate();
    store.closeEditor();
    expect(store.editor()).toBeNull();
  });
});
