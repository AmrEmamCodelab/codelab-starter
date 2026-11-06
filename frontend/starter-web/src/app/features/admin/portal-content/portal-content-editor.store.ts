import { Injectable, computed, inject, signal } from '@angular/core';
import type { ApiError } from '@codelab/ng-security';
import { firstValueFrom } from 'rxjs';
import { NO_FIELD_ERRORS, hasFieldErrors, type FieldErrorMap } from '../../../patterns';
import { apiErrorFrom } from '../organizations/data/admin-api-error';
import { PortalContentAdminClient } from './data/portal-content-api';
import {
  PortalContentLimits,
  PortalContentPages,
  type PortalContentDocument,
  type PortalContentPage,
} from './data/portal-content.models';
import {
  draftsOf,
  moveSection,
  removeSection,
  replaceBodyOf,
  sameSections,
  sectionErrorIndexes,
  sectionErrors,
  trimmedTexts,
  upsertSection,
  type DraftSection,
} from './section-draft';
import { SectionForm } from './section-form';

/** One place up or down the list. */
const MOVE_UP = -1;
const MOVE_DOWN = 1;

/** Asks the user whether to drop unsaved edits; resolves true to drop them. */
export type ConfirmDiscard = () => Promise<boolean>;

/**
 * The state of the portal content editor (provided per page): the selected page, its stored sections and the draft
 * the admin edits (add, edit in the section dialog, remove, reorder), the save (one PUT that replaces the page's
 * sections in list order) and the dirty flag the leave guard and the page selector ask before dropping edits.
 */
@Injectable()
export class PortalContentEditorStore {
  private readonly client = inject(PortalContentAdminClient);
  private nextKey = 0;
  private latestLoad = 0;

  private readonly selectedPage = signal<PortalContentPage>(PortalContentPages.Faq);
  private readonly stored = signal<readonly DraftSection[]>([]);
  private readonly draft = signal<readonly DraftSection[]>([]);
  private readonly storedAt = signal<string | null>(null);
  private readonly loadingNow = signal(false);
  private readonly loadFailure = signal<ApiError | null>(null);
  private readonly savingNow = signal(false);
  private readonly saveFailure = signal<ApiError | null>(null);
  private readonly savedNow = signal(false);
  private readonly editorForm = signal<SectionForm | null>(null);
  private readonly editorFieldErrors = signal<FieldErrorMap>(NO_FIELD_ERRORS);

  readonly page = this.selectedPage.asReadonly();
  readonly sections = this.draft.asReadonly();
  /** ISO 8601 UTC time of the last save of the page, or null. */
  readonly updatedAt = this.storedAt.asReadonly();
  readonly loading = this.loadingNow.asReadonly();
  readonly loadError = this.loadFailure.asReadonly();
  readonly saving = this.savingNow.asReadonly();
  readonly saveError = this.saveFailure.asReadonly();
  /** True after a successful save, until the next edit. */
  readonly saved = this.savedNow.asReadonly();
  readonly editor = this.editorForm.asReadonly();
  readonly editorErrors = this.editorFieldErrors.asReadonly();
  readonly dirty = computed(() => !sameSections(this.draft(), this.stored()));
  readonly canAdd = computed(() => this.draft().length < PortalContentLimits.MaxSections);
  /** The list positions of the sections the last refused save named. */
  readonly invalidIndexes = computed(() => sectionErrorIndexes(this.saveFailure()));

  /** Loads `page` and replaces the draft with its stored sections. Only the latest load's answer is kept. */
  async load(page: PortalContentPage): Promise<void> {
    const request = ++this.latestLoad;
    this.selectedPage.set(page);
    this.loadingNow.set(true);
    this.loadFailure.set(null);
    this.reset([], null);
    try {
      const document = await firstValueFrom(this.client.get(page));
      if (request === this.latestLoad) {
        this.accept(document);
      }
    } catch (error: unknown) {
      if (request === this.latestLoad) {
        this.loadFailure.set(apiErrorFrom(error));
      }
    } finally {
      if (request === this.latestLoad) {
        this.loadingNow.set(false);
      }
    }
  }

  /**
   * Switches to `page`. With unsaved edits it first asks `confirmDiscard`; answering "stay" keeps the current page
   * and resolves false. Choosing the page already shown does nothing and resolves true.
   */
  async selectPage(page: PortalContentPage, confirmDiscard: ConfirmDiscard): Promise<boolean> {
    if (page === this.selectedPage()) {
      return true;
    }
    if (this.dirty() && !(await confirmDiscard())) {
      return false;
    }
    await this.load(page);
    return true;
  }

  openAdd(): void {
    if (this.canAdd()) {
      this.openEditor(SectionForm.blank(this.takeKey()));
    }
  }

  openEdit(section: DraftSection): void {
    this.openEditor(SectionForm.of(section));
  }

  closeEditor(): void {
    this.editorForm.set(null);
  }

  /** Checks the dialog's fields; when valid, puts the section into the draft and closes the dialog. */
  applyEditor(): void {
    const form = this.editorForm();
    if (form === null) {
      return;
    }
    const errors = sectionErrors(form.texts());
    this.editorFieldErrors.set(errors);
    if (hasFieldErrors(errors)) {
      return;
    }
    this.edit(upsertSection(this.draft(), form.toDraft(trimmedTexts(form.texts()))));
    this.editorForm.set(null);
  }

  moveUp(index: number): void {
    this.edit(moveSection(this.draft(), index, MOVE_UP));
  }

  moveDown(index: number): void {
    this.edit(moveSection(this.draft(), index, MOVE_DOWN));
  }

  remove(section: DraftSection): void {
    this.edit(removeSection(this.draft(), section.key));
  }

  /** Drops every unsaved edit. */
  discard(): void {
    this.draft.set(this.stored());
    this.saveFailure.set(null);
  }

  /** Replaces the page's sections with the draft; on success the stored page becomes the new baseline. */
  async save(): Promise<void> {
    if (this.savingNow() || !this.dirty()) {
      return;
    }
    this.savingNow.set(true);
    this.saveFailure.set(null);
    try {
      const document = await firstValueFrom(this.client.replace(this.selectedPage(), replaceBodyOf(this.draft())));
      this.accept(document);
      this.savedNow.set(true);
    } catch (error: unknown) {
      this.saveFailure.set(apiErrorFrom(error));
    } finally {
      this.savingNow.set(false);
    }
  }

  private accept(document: PortalContentDocument): void {
    this.reset(draftsOf(document, () => this.takeKey()), document.updatedAt);
  }

  private reset(sections: readonly DraftSection[], updatedAt: string | null): void {
    this.stored.set(sections);
    this.draft.set(sections);
    this.storedAt.set(updatedAt);
    this.saveFailure.set(null);
    this.savedNow.set(false);
    this.editorForm.set(null);
  }

  private edit(sections: readonly DraftSection[]): void {
    this.draft.set(sections);
    this.savedNow.set(false);
  }

  private openEditor(form: SectionForm): void {
    this.editorFieldErrors.set(NO_FIELD_ERRORS);
    this.editorForm.set(form);
  }

  private takeKey(): number {
    this.nextKey += 1;
    return this.nextKey;
  }
}
