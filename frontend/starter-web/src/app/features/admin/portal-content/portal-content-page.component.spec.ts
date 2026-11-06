import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { fakeLanguage } from '../../../layouts/layout.testing';
import { adminTestProviders, allByTestId, byTestId, grant, press, problem, settle } from '../organizations/admin.testing';
import { PortalContentAdminClient } from './data/portal-content-api';
import { PortalContentPages } from './data/portal-content.models';
import { PortalContentPageComponent } from './portal-content-page.component';
import { PortalContentPermissionKeys } from './portal-content-permissions';
import { contentDocument } from './portal-content.testing';

describe('PortalContentPageComponent', () => {
  let fixture: ComponentFixture<PortalContentPageComponent>;
  let host: HTMLElement;
  let client: { get: ReturnType<typeof vi.fn>; replace: ReturnType<typeof vi.fn> };

  async function render(): Promise<void> {
    fixture = TestBed.createComponent(PortalContentPageComponent);
    host = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    await settle(fixture);
  }

  function submitDialog(): void {
    const dialog = host.querySelector('dialog[open]') as HTMLElement;
    (dialog.querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
  }

  function type(testId: string, text: string): void {
    const field = byTestId(fixture, testId)?.querySelector('input, textarea') as HTMLInputElement | HTMLTextAreaElement;
    field.value = text;
    field.dispatchEvent(new Event('input'));
  }

  function tab(label: string): HTMLButtonElement {
    const tabs = [...host.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
    const found = tabs.find((candidate) => candidate.textContent?.includes(label));
    if (!found) {
      throw new Error(`No tab ${label}.`);
    }
    return found;
  }

  beforeEach(() => {
    client = { get: vi.fn().mockReturnValue(of(contentDocument())), replace: vi.fn().mockReturnValue(of(contentDocument())) };
    TestBed.configureTestingModule({
      imports: [PortalContentPageComponent],
      providers: [
        ...adminTestProviders(fakeLanguage(AppLanguages.English)),
        { provide: PortalContentAdminClient, useValue: client },
      ],
    });
  });

  it('shows the FAQ sections in order, read-only for a viewer', async () => {
    grant(PortalContentPermissionKeys.View);
    await render();
    expect(client.get).toHaveBeenCalledWith(PortalContentPages.Faq);
    const rows = host.querySelectorAll('tbody tr');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('First');
    expect(byTestId(fixture, 'content-read-only')).not.toBeNull();
    expect(byTestId(fixture, 'content-section-add')).toBeNull();
    expect(byTestId(fixture, 'content-save')).toBeNull();
    expect(allByTestId(fixture, 'content-section-remove')).toHaveLength(0);
    expect(byTestId(fixture, 'content-updated-at')?.textContent).toContain('admin.portal-content.updated-at|at=');

    press(allByTestId(fixture, 'content-section-open')[0]);
    await settle(fixture);
    expect(host.querySelector('dialog[open]')?.textContent).toContain('admin.portal-content.editor.view-title');
    submitDialog();
    await settle(fixture);
    expect(host.querySelector('dialog[open]')).toBeNull();
  });

  it('adds a section through the dialog, showing client checks first, then saves the page', async () => {
    grant(PortalContentPermissionKeys.View, PortalContentPermissionKeys.Manage);
    await render();
    press(byTestId(fixture, 'content-section-add'));
    await settle(fixture);
    submitDialog();
    await settle(fixture);
    expect(host.querySelector('dialog[open]')?.textContent).toContain('errors.REQUIRED');

    type('section-title-ar', 'سؤال');
    type('section-title-en', 'Question');
    type('section-body-ar', 'جواب');
    type('section-body-en', 'Answer');
    submitDialog();
    await settle(fixture);
    expect(host.querySelector('dialog[open]')).toBeNull();
    expect(host.querySelectorAll('tbody tr')).toHaveLength(3);
    expect(byTestId(fixture, 'content-unsaved')).not.toBeNull();

    press(byTestId(fixture, 'content-save'));
    await settle(fixture);
    expect(client.replace).toHaveBeenCalledWith(PortalContentPages.Faq, {
      sections: [
        expect.objectContaining({ id: 'sec-1', sortOrder: 1 }),
        expect.objectContaining({ id: 'sec-2', sortOrder: 2 }),
        expect.objectContaining({ id: null, titleEn: 'Question', bodyAr: 'جواب', sortOrder: 3 }),
      ],
    });
    expect(byTestId(fixture, 'content-saved')).not.toBeNull();
  });

  it('reorders and removes rows, and marks the sections a refused save names', async () => {
    client.replace.mockReturnValue(throwError(() =>
      problem(HttpStatus.badRequest, 'VALIDATION_FAILED', { 'sections[0].titleEn': ['TOO_LONG'] })));
    grant(PortalContentPermissionKeys.View, PortalContentPermissionKeys.Manage);
    await render();
    press(allByTestId(fixture, 'content-section-down')[0]);
    await settle(fixture);
    expect(host.querySelectorAll('tbody tr')[0].textContent).toContain('Second');
    press(allByTestId(fixture, 'content-section-remove')[1]);
    await settle(fixture);
    expect(host.querySelectorAll('tbody tr')).toHaveLength(1);

    press(byTestId(fixture, 'content-save'));
    await settle(fixture);
    expect(byTestId(fixture, 'content-save-error')).not.toBeNull();
    expect(byTestId(fixture, 'content-section-invalid')).not.toBeNull();

    press(byTestId(fixture, 'content-discard'));
    await settle(fixture);
    expect(host.querySelectorAll('tbody tr')).toHaveLength(2);
    expect(byTestId(fixture, 'content-save-error')).toBeNull();
  });

  it('asks before switching pages with unsaved edits and stays on "stay"', async () => {
    grant(PortalContentPermissionKeys.View, PortalContentPermissionKeys.Manage);
    await render();
    press(allByTestId(fixture, 'content-section-down')[0]);
    await settle(fixture);
    tab('admin.portal-content.pages.terms').click();
    await settle(fixture);
    expect(host.querySelector('dialog[open]')?.textContent).toContain('admin.access.leave.title');

    const leaveDialog = host.querySelector('dialog[open]') as HTMLElement;
    leaveDialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle(fixture);
    expect(client.get).toHaveBeenCalledTimes(1);
    expect(tab('admin.portal-content.pages.faq').getAttribute('aria-selected')).toBe('true');

    tab('admin.portal-content.pages.privacy').click();
    await settle(fixture);
    submitDialog();
    await settle(fixture);
    expect(client.get).toHaveBeenLastCalledWith(PortalContentPages.Privacy);
    expect(tab('admin.portal-content.pages.privacy').getAttribute('aria-selected')).toBe('true');
  });

  it('lets the route leave at once when clean, and asks when dirty', async () => {
    grant(PortalContentPermissionKeys.View, PortalContentPermissionKeys.Manage);
    await render();
    expect(fixture.componentInstance.canLeave()).toBe(true);
    press(allByTestId(fixture, 'content-section-down')[0]);
    await settle(fixture);
    const answer = fixture.componentInstance.canLeave();
    expect(answer).toBeInstanceOf(Promise);
    const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
    fixture.componentInstance.onBeforeUnload(event);
    expect(event.defaultPrevented).toBe(true);
  });

  it('shows a load failure in the list', async () => {
    client.get.mockReturnValue(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
    grant(PortalContentPermissionKeys.View, PortalContentPermissionKeys.Manage);
    await render();
    expect(host.querySelector('[role="alert"]')).not.toBeNull();
    expect(byTestId(fixture, 'content-section-add')?.querySelector('button')?.disabled).toBe(true);
  });
});
