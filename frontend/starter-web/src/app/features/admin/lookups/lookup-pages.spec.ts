import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { fakeLanguage } from '../../../layouts/layout.testing';
import { adminTestProviders, allByTestId, byTestId, grant, press, problem, settle } from '../organizations/admin.testing';
import { LookupsAdminClient } from './data/lookups-api';
import { LookupItemsPageComponent } from './lookup-items-page.component';
import { LookupTypesPageComponent } from './lookup-types-page.component';
import { LookupsPermissionKeys } from './lookups-permissions';
import { lookupItem, lookupType } from './lookups.testing';

type FakeClient = Record<'types' | 'items' | 'setActive', ReturnType<typeof vi.fn>>;

function fakeClient(): FakeClient {
  return {
    types: vi.fn().mockReturnValue(of([lookupType(), lookupType({ type: 'request-categories', nameEn: 'Request categories' })])),
    items: vi.fn().mockReturnValue(of([
      lookupItem({ id: 2, code: 'ACCOUNT', sortOrder: 2, isActive: false }),
      lookupItem({ id: 1, code: 'GENERAL', sortOrder: 1 }),
    ])),
    setActive: vi.fn().mockReturnValue(of(undefined)),
  };
}

describe('LookupTypesPageComponent', () => {
  let client: FakeClient;

  beforeEach(() => {
    client = fakeClient();
    TestBed.configureTestingModule({
      imports: [LookupTypesPageComponent],
      providers: [
        ...adminTestProviders(fakeLanguage(AppLanguages.English)),
        { provide: LookupsAdminClient, useValue: client },
      ],
    });
  });

  it('lists the types with their names in the current language, linked to their values', async () => {
    grant(LookupsPermissionKeys.View);
    const fixture = TestBed.createComponent(LookupTypesPageComponent);
    fixture.detectChanges();
    await settle(fixture);
    const links = allByTestId(fixture, 'lookup-type-link');
    expect(links.map((link) => link.textContent?.trim())).toEqual(['Contact topics', 'Request categories']);
    expect(links[0].getAttribute('href')).toBe('/admin/lookups/contact-topics');
    expect(byTestId(fixture, 'lookups-structure-note')?.textContent).toContain('admin.lookups.structure-note');
  });

  it('shows the load failure in the list', async () => {
    client.types.mockReturnValue(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
    const fixture = TestBed.createComponent(LookupTypesPageComponent);
    fixture.detectChanges();
    await settle(fixture);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).not.toBeNull();
  });
});

describe('LookupItemsPageComponent', () => {
  let client: FakeClient;
  let fixture: ComponentFixture<LookupItemsPageComponent>;
  let host: HTMLElement;

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(LookupItemsPageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  beforeEach(() => {
    client = fakeClient();
    TestBed.configureTestingModule({
      imports: [LookupItemsPageComponent],
      providers: [
        ...adminTestProviders(fakeLanguage(AppLanguages.English)),
        { provide: LookupsAdminClient, useValue: client },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ type: 'contact-topics' }) } } },
      ],
    });
  });

  it('lists the values of the routed type by sort order, with status tags and the type name', async () => {
    grant(LookupsPermissionKeys.View);
    await render();
    expect(client.items).toHaveBeenCalledWith('contact-topics');
    const rows = host.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('GENERAL');
    expect(rows[0].textContent).toContain('admin.common.status.active');
    expect(rows[1].textContent).toContain('admin.common.status.inactive');
    expect(byTestId(fixture, 'lookup-type-name')?.textContent).toContain('name=Contact topics');
  });

  it('shows the type key until the type names arrive', async () => {
    client.types.mockReturnValue(new Subject());
    await render();
    expect(byTestId(fixture, 'lookup-type-name')?.textContent).toContain('name=contact-topics');
  });

  it('hides the activate / deactivate action from a viewer', async () => {
    grant(LookupsPermissionKeys.View);
    await render();
    expect(allByTestId(fixture, 'lookup-item-status')).toHaveLength(0);
  });

  it('deactivates a value after confirmation and reloads', async () => {
    grant(LookupsPermissionKeys.View, LookupsPermissionKeys.Manage);
    await render();
    const actions = allByTestId(fixture, 'lookup-item-status');
    expect(actions.map((action) => action.textContent?.trim())).toEqual([
      'admin.lookups.items.deactivate',
      'admin.lookups.items.activate',
    ]);
    press(actions[0]);
    await settle(fixture);
    expect(client.setActive).not.toHaveBeenCalled();
    const dialog = host.querySelector('dialog[open]') as HTMLElement;
    expect(byTestId(fixture, 'lookup-status-text')?.textContent).toContain('admin.lookups.items.deactivate-text|code=GENERAL');
    (dialog.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(client.setActive).toHaveBeenCalledWith('contact-topics', 1, false);
    expect(client.items).toHaveBeenCalledTimes(2);
    expect(host.querySelector('dialog[open]')).toBeNull();
  });

  it('keeps the dialog open with the refusal when the write fails, and cancels', async () => {
    client.setActive.mockReturnValue(throwError(() => problem(HttpStatus.notFound, 'NOT_FOUND')));
    grant(LookupsPermissionKeys.View, LookupsPermissionKeys.Manage);
    await render();
    press(allByTestId(fixture, 'lookup-item-status')[1]);
    await settle(fixture);
    const dialog = host.querySelector('dialog[open]') as HTMLElement;
    (dialog.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(client.setActive).toHaveBeenCalledWith('contact-topics', 2, true);
    expect(host.querySelector('dialog[open]')).not.toBeNull();
    expect(dialog.textContent).toContain('errors.NOT_FOUND');
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle(fixture);
    expect(host.querySelector('dialog[open]')).toBeNull();
  });

  it('shows a not-found type as the list error', async () => {
    client.items.mockReturnValue(throwError(() => problem(HttpStatus.notFound, 'NOT_FOUND')));
    await render();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('errors.NOT_FOUND');
  });
});
