import { describe, expect, it } from 'vitest';
import { AppLanguages } from '../../../core/i18n/language';
import { SECTION_ID_PATTERN } from '../../../patterns';
import { aContentSection } from '../portal.testing';
import { CONTENT_ANCHOR_PREFIX, contentTocSections, localizeSections, splitParagraphs } from './portal-content';

describe('splitParagraphs', () => {
  it('splits on blank lines, trims and drops empty paragraphs', () => {
    expect(splitParagraphs('First line\n\nSecond\r\n\r\nThird\n  \nFourth')).toEqual(['First line', 'Second', 'Third', 'Fourth']);
    expect(splitParagraphs('  \n\n  ')).toEqual([]);
    expect(splitParagraphs('')).toEqual([]);
  });

  it('keeps a single line break inside its paragraph', () => {
    expect(splitParagraphs('Line one\nline two\n\nNext')).toEqual(['Line one\nline two', 'Next']);
  });
});

describe('localizeSections', () => {
  const SECTIONS = [
    aContentSection({ id: 'b', titleAr: 'الثاني', titleEn: ' Second ', bodyAr: 'أ\n\nب', bodyEn: 'A\n\nB', sortOrder: 2 }),
    aContentSection({ id: 'a', titleAr: 'الأول', titleEn: 'First', bodyAr: 'نص', bodyEn: 'Text', sortOrder: 1 }),
    aContentSection({ id: 'c', titleAr: 'الثالث', titleEn: 'Third', sortOrder: 2 }),
  ];

  it('orders by sortOrder, keeping the server order on ties', () => {
    expect(localizeSections(SECTIONS, AppLanguages.English).map(section => section.id)).toEqual(['a', 'b', 'c']);
  });

  it('picks the active language and splits the body into paragraphs', () => {
    const [first, second] = localizeSections(SECTIONS, AppLanguages.Arabic);
    expect(first?.title).toBe('الأول');
    expect(second?.paragraphs).toEqual(['أ', 'ب']);
    expect(localizeSections(SECTIONS, AppLanguages.English)[1]).toMatchObject({ title: 'Second', paragraphs: ['A', 'B'] });
  });

  it('gives each section an anchor-safe slug by position, never from the server id', () => {
    const anchors = localizeSections([aContentSection({ id: 'Opaque_ID+1' }), aContentSection({ id: 'x' })], AppLanguages.Arabic)
      .map(section => section.anchor);
    expect(anchors).toEqual([`${CONTENT_ANCHOR_PREFIX}1`, `${CONTENT_ANCHOR_PREFIX}2`]);
    for (const anchor of anchors) {
      expect(anchor).toMatch(SECTION_ID_PATTERN);
    }
  });

  it('lists the sections for the table of contents by anchor and title', () => {
    const sections = localizeSections(SECTIONS, AppLanguages.English);
    expect(contentTocSections(sections)).toEqual([
      { id: 'part-1', titleKey: 'First' },
      { id: 'part-2', titleKey: 'Second' },
      { id: 'part-3', titleKey: 'Third' },
    ]);
  });
});
