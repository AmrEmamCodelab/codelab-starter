import { signal } from '@angular/core';
import type { DraftSection, SectionTexts } from './section-draft';

/** The section editor dialog's fields as signals (bound two-way to the inputs), for one new or existing section. */
export class SectionForm {
  readonly titleAr = signal('');
  readonly titleEn = signal('');
  readonly bodyAr = signal('');
  readonly bodyEn = signal('');

  private constructor(
    /** The client key of the draft it edits (a fresh one for a new section). */
    readonly key: number,
    /** The stored id, or null for a section not saved yet. */
    readonly id: string | null,
    /** True when the dialog adds a section rather than editing one in the list. */
    readonly isNew: boolean,
  ) {}

  static blank(key: number): SectionForm {
    return new SectionForm(key, null, true);
  }

  static of(draft: DraftSection): SectionForm {
    const form = new SectionForm(draft.key, draft.id, false);
    form.titleAr.set(draft.titleAr);
    form.titleEn.set(draft.titleEn);
    form.bodyAr.set(draft.bodyAr);
    form.bodyEn.set(draft.bodyEn);
    return form;
  }

  texts(): SectionTexts {
    return { titleAr: this.titleAr(), titleEn: this.titleEn(), bodyAr: this.bodyAr(), bodyEn: this.bodyEn() };
  }

  /** The draft these fields describe, with `texts` (already trimmed by the caller). */
  toDraft(texts: SectionTexts): DraftSection {
    return { key: this.key, id: this.id, ...texts };
  }
}
