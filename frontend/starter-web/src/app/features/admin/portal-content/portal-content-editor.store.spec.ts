import { TestBed } from '@angular/core/testing';
import { HttpStatus } from '@codelab/ng-security';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { problem } from '../organizations/admin.testing';
import { PortalContentAdminClient } from './data/portal-content-api';
import { PortalContentLimits, PortalContentPages, type PortalContentDocument } from './data/portal-content.models';
import { PortalContentEditorStore } from './portal-content-editor.store';
import { contentDocument, contentSection } from './portal-content.testing';

describe('PortalContentEditorStore', () => {
  let client: { get: ReturnType<typeof vi.fn>; replace: ReturnType<typeof vi.fn> };
  let store: PortalContentEditorStore;

  beforeEach(async () => {
    client = { get: vi.fn().mockReturnValue(of(contentDocument())), replace: vi.fn().mockReturnValue(of(contentDocument())) };
    TestBed.configureTestingModule({
      providers: [PortalContentEditorStore, { provide: PortalContentAdminClient, useValue: client }],
    });
    store = TestBed.inject(PortalContentEditorStore);
    await store.load(PortalContentPages.Faq);
  });

  function fill(texts: Partial<Record<'titleAr' | 'titleEn' | 'bodyAr' | 'bodyEn', string>>): void {
    const form = store.editor();
    if (!form) {
      throw new Error('No editor.');
    }
    form.titleAr.set(texts.titleAr ?? '');
    form.titleEn.set(texts.titleEn ?? '');
    form.bodyAr.set(texts.bodyAr ?? '');
    form.bodyEn.set(texts.bodyEn ?? '');
  }

  it('loads a page in sort order, clean', () => {
    expect(client.get).toHaveBeenCalledWith(PortalContentPages.Faq);
    expect(store.sections().map((section) => section.titleEn)).toEqual(['First', 'Second']);
    expect(store.updatedAt()).toBe('2026-11-01T09:30:00Z');
    expect(store.dirty()).toBe(false);
  });

  it('keeps a load failure and only the latest load', async () => {
    const slow = new Subject<PortalContentDocument>();
    client.get.mockReturnValueOnce(slow).mockReturnValueOnce(throwError(() => problem(HttpStatus.serverError, 'INTERNAL_ERROR')));
    const first = store.load(PortalContentPages.Privacy);
    await store.load(PortalContentPages.Terms);
    slow.next(contentDocument());
    slow.complete();
    await first;
    expect(store.page()).toBe(PortalContentPages.Terms);
    expect(store.loadError()?.status).toBe(HttpStatus.serverError);
    expect(store.sections()).toEqual([]);
    expect(store.loading()).toBe(false);
  });

  it('adds a valid section, trimmed, and marks the page dirty', () => {
    store.openAdd();
    expect(store.editor()?.isNew).toBe(true);
    fill({ titleAr: ' ع ', titleEn: 'New', bodyAr: 'ن', bodyEn: 'B' });
    store.applyEditor();
    expect(store.editor()).toBeNull();
    expect(store.sections().at(-1)).toMatchObject({ id: null, titleAr: 'ع', titleEn: 'New' });
    expect(store.dirty()).toBe(true);
  });

  it('keeps the dialog open with field errors for an invalid section', () => {
    store.openAdd();
    fill({ titleEn: 'Only English' });
    store.applyEditor();
    expect(store.editor()).not.toBeNull();
    expect(Object.keys(store.editorErrors()).sort()).toEqual(['bodyAr', 'bodyEn', 'titleAr']);
    expect(store.dirty()).toBe(false);
  });

  it('edits in place, reorders, removes and discards', () => {
    const [first, second] = store.sections();
    store.openEdit(first);
    store.editor()?.titleEn.set('First edited');
    store.applyEditor();
    expect(store.sections()[0].titleEn).toBe('First edited');
    store.moveDown(0);
    expect(store.sections().map((section) => section.id)).toEqual([second.id, first.id]);
    store.moveUp(1);
    store.remove(second);
    expect(store.sections().map((section) => section.id)).toEqual([first.id]);
    store.discard();
    expect(store.sections().map((section) => section.titleEn)).toEqual(['First', 'Second']);
    expect(store.dirty()).toBe(false);
  });

  it('does not offer to add past the maximum number of sections', async () => {
    const sections = Array.from({ length: PortalContentLimits.MaxSections }, (_, index) =>
      contentSection({ id: `s${index}`, sortOrder: index + 1 }));
    client.get.mockReturnValue(of(contentDocument({ sections })));
    await store.load(PortalContentPages.Terms);
    expect(store.canAdd()).toBe(false);
    store.openAdd();
    expect(store.editor()).toBeNull();
  });

  it('saves the whole page in list order and takes the answer as the new baseline', async () => {
    store.moveDown(0);
    const saved = contentDocument({ updatedAt: '2026-11-06T10:00:00Z', sections: [contentSection({ id: 'sec-2', titleEn: 'Second' })] });
    client.replace.mockReturnValue(of(saved));
    await store.save();
    expect(client.replace).toHaveBeenCalledWith(PortalContentPages.Faq, {
      sections: [
        expect.objectContaining({ id: 'sec-2', sortOrder: 1 }),
        expect.objectContaining({ id: 'sec-1', sortOrder: 2 }),
      ],
    });
    expect(store.dirty()).toBe(false);
    expect(store.saved()).toBe(true);
    expect(store.updatedAt()).toBe('2026-11-06T10:00:00Z');
    expect(store.sections()).toHaveLength(1);
  });

  it('keeps the draft and names the refused sections when the save fails', async () => {
    store.moveDown(0);
    client.replace.mockReturnValue(throwError(() =>
      problem(HttpStatus.badRequest, 'VALIDATION_FAILED', { 'sections[1].bodyAr': ['TOO_LONG'] })));
    await store.save();
    expect(store.saveError()?.code).toBeTruthy();
    expect([...store.invalidIndexes()]).toEqual([1]);
    expect(store.dirty()).toBe(true);
    expect(store.saving()).toBe(false);
  });

  it('does not save a clean page', async () => {
    await store.save();
    expect(client.replace).not.toHaveBeenCalled();
  });

  it('switches pages at once when clean, and asks first when dirty', async () => {
    const confirm = vi.fn().mockResolvedValue(false);
    expect(await store.selectPage(PortalContentPages.Faq, confirm)).toBe(true);
    expect(await store.selectPage(PortalContentPages.Privacy, confirm)).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
    expect(store.page()).toBe(PortalContentPages.Privacy);

    store.moveDown(0);
    expect(await store.selectPage(PortalContentPages.Terms, confirm)).toBe(false);
    expect(store.page()).toBe(PortalContentPages.Privacy);
    expect(store.dirty()).toBe(true);

    confirm.mockResolvedValue(true);
    expect(await store.selectPage(PortalContentPages.Terms, confirm)).toBe(true);
    expect(store.page()).toBe(PortalContentPages.Terms);
    expect(store.dirty()).toBe(false);
  });
});
