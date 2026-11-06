import { HttpStatus, type ApiError } from '@codelab/ng-security';
import { describe, expect, it } from 'vitest';
import { PortalContentLimits, isPortalContentPage } from './data/portal-content.models';
import { contentDocument, draftSection } from './portal-content.testing';
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
} from './section-draft';
import { SectionForm } from './section-form';

const A = draftSection({ key: 1, id: 'a', titleEn: 'A' });
const B = draftSection({ key: 2, id: 'b', titleEn: 'B' });
const C = draftSection({ key: 3, id: null, titleEn: 'C' });

describe('section drafts', () => {
  it('takes a stored page in sort order, giving each row a client key', () => {
    const drafts = draftsOf(contentDocument(), (index) => index + 10);
    expect(drafts.map((draft) => [draft.key, draft.id, draft.titleEn])).toEqual([[10, 'sec-1', 'First'], [11, 'sec-2', 'Second']]);
  });

  it('numbers the replace body in list order from 1', () => {
    const body = replaceBodyOf([B, C]);
    expect(body.sections.map((section) => [section.id, section.sortOrder])).toEqual([['b', 1], [null, 2]]);
    expect(Object.keys(body.sections[0]).sort()).toEqual(['bodyAr', 'bodyEn', 'id', 'sortOrder', 'titleAr', 'titleEn']);
  });

  it('compares lists by ids, texts and order, not by client keys', () => {
    expect(sameSections([A, B], [{ ...A, key: 9 }, { ...B, key: 8 }])).toBe(true);
    expect(sameSections([A, B], [B, A])).toBe(false);
    expect(sameSections([A], [{ ...A, bodyAr: 'changed' }])).toBe(false);
    expect(sameSections([A], [A, B])).toBe(false);
  });

  it('moves a section by one place and ignores a move past either end', () => {
    expect(moveSection([A, B, C], 1, -1)).toEqual([B, A, C]);
    expect(moveSection([A, B, C], 1, 1)).toEqual([A, C, B]);
    const list = [A, B];
    expect(moveSection(list, 0, -1)).toBe(list);
    expect(moveSection(list, 1, 1)).toBe(list);
    expect(moveSection(list, 5, -1)).toBe(list);
  });

  it('removes by key and upserts by key', () => {
    expect(removeSection([A, B], B.key)).toEqual([A]);
    const renamed = { ...A, titleEn: 'A2' };
    expect(upsertSection([A, B], renamed)).toEqual([renamed, B]);
    expect(upsertSection([A], C)).toEqual([A, C]);
  });

  it('trims the ends of each text and keeps inner blank lines', () => {
    expect(trimmedTexts({ titleAr: ' t ', titleEn: 'e ', bodyAr: '\n p1\n\np2 \n', bodyEn: 'b' }))
      .toEqual({ titleAr: 't', titleEn: 'e', bodyAr: 'p1\n\np2', bodyEn: 'b' });
  });
});

describe('section checks', () => {
  const valid = { titleAr: 'ع', titleEn: 'T', bodyAr: 'ن', bodyEn: 'B' };

  it('accepts a complete section', () => {
    expect(sectionErrors(valid)).toEqual({});
  });

  it('requires every text (blank counts as empty)', () => {
    expect(sectionErrors({ ...valid, titleAr: '  ', bodyEn: '' })).toEqual({
      titleAr: ['errors.REQUIRED'],
      bodyEn: ['errors.REQUIRED'],
    });
  });

  it('refuses titles and bodies over the limits, and accepts them at the limits', () => {
    const title = 'x'.repeat(PortalContentLimits.TitleMaxLength);
    const body = 'x'.repeat(PortalContentLimits.BodyMaxLength);
    expect(sectionErrors({ ...valid, titleEn: title, bodyAr: body })).toEqual({});
    expect(sectionErrors({ ...valid, titleEn: `${title}x`, bodyAr: `${body}x` })).toEqual({
      titleEn: ['errors.TOO_LONG'],
      bodyAr: ['errors.TOO_LONG'],
    });
  });

  it('finds the sections a refused save names, in any case', () => {
    const error: ApiError = {
      status: HttpStatus.badRequest,
      code: 'VALIDATION_FAILED',
      fieldErrors: [
        { field: 'sections[2].titleAr', message: 'TOO_LONG' },
        { field: 'Sections[0].BodyEn', message: 'REQUIRED' },
        { field: 'sections', message: 'TOO_MANY' },
      ],
    };
    expect([...sectionErrorIndexes(error)].sort()).toEqual([0, 2]);
    expect(sectionErrorIndexes(null).size).toBe(0);
  });
});

describe('SectionForm', () => {
  it('starts blank for a new section and copies an existing one', () => {
    const blank = SectionForm.blank(7);
    expect([blank.key, blank.id, blank.isNew]).toEqual([7, null, true]);
    expect(blank.texts()).toEqual({ titleAr: '', titleEn: '', bodyAr: '', bodyEn: '' });
    const form = SectionForm.of(A);
    expect(form.isNew).toBe(false);
    form.titleEn.set('Changed');
    expect(form.toDraft(form.texts())).toEqual({ ...A, titleEn: 'Changed' });
  });

  it('recognises the editable pages only', () => {
    expect(isPortalContentPage('faq')).toBe(true);
    expect(isPortalContentPage('contact')).toBe(false);
    expect(isPortalContentPage(1)).toBe(false);
  });
});
