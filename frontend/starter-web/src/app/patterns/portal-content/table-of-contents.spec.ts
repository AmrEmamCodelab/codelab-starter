import { describe, expect, it } from 'vitest';
import { PORTAL_CONTENT_TRANSLATION_KEYS } from './portal-content-translation-keys';
import { TocDepth, activeSection, buildToc, sectionAnchorId, tocHref } from './table-of-contents';

describe('buildToc', () => {
  it('lists the sections in order with their anchors and depths', () => {
    expect(buildToc([
      { id: 'about', titleKey: 'guide.about' },
      { id: 'documents', titleKey: 'guide.documents' },
      { id: 'documents-optional', titleKey: 'guide.documents-optional', depth: TocDepth.Subsection },
    ])).toEqual([
      { id: 'about', anchorId: 'section-about', titleKey: 'guide.about', depth: TocDepth.Section },
      { id: 'documents', anchorId: 'section-documents', titleKey: 'guide.documents', depth: TocDepth.Section },
      { id: 'documents-optional', anchorId: 'section-documents-optional', titleKey: 'guide.documents-optional', depth: TocDepth.Subsection },
    ]);
  });

  it('leaves out invalid and repeated ids', () => {
    const toc = buildToc([
      { id: 'about', titleKey: 'a' },
      { id: 'About Us', titleKey: 'b' },
      { id: '1st', titleKey: 'c' },
      { id: 'about', titleKey: 'd' },
      { id: '', titleKey: 'e' },
    ]);
    expect(toc.map((entry) => entry.titleKey)).toEqual(['a']);
  });

  it('shows a leading subsection as a section', () => {
    const [first] = buildToc([{ id: 'intro', titleKey: 'x', depth: TocDepth.Subsection }]);
    expect(first.depth).toBe(TocDepth.Section);
  });
});

describe('sectionAnchorId and tocHref', () => {
  it('prefixes the anchor and keeps the page path and query', () => {
    expect(sectionAnchorId('faq')).toBe('section-faq');
    expect(tocHref('/portal/service-guide?lang=en', 'section-faq')).toBe('/portal/service-guide?lang=en#section-faq');
    expect(tocHref('/portal/terms#section-old', 'section-new')).toBe('/portal/terms#section-new');
  });
});

describe('activeSection', () => {
  const toc = buildToc([
    { id: 'a', titleKey: 'a' },
    { id: 'b', titleKey: 'b' },
    { id: 'c', titleKey: 'c' },
  ]);

  it('is the last section whose top passed the offset', () => {
    const tops = new Map([['section-a', -400], ['section-b', 50], ['section-c', 600]]);
    expect(activeSection(toc, tops, 96)).toBe('section-b');
  });

  it('is the first section before any reached the offset, and null without sections', () => {
    expect(activeSection(toc, new Map([['section-a', 300]]), 96)).toBe('section-a');
    expect(activeSection([], new Map(), 96)).toBeNull();
  });

  it('skips sections that are not on the page', () => {
    expect(activeSection(toc, new Map([['section-a', -10], ['section-c', -5]]), 96)).toBe('section-c');
  });
});

describe('PortalContentTranslationKeys', () => {
  it('has unique keys under patterns.portal-content.*', () => {
    expect(new Set(PORTAL_CONTENT_TRANSLATION_KEYS).size).toBe(PORTAL_CONTENT_TRANSLATION_KEYS.length);
    for (const key of PORTAL_CONTENT_TRANSLATION_KEYS) {
      expect(key).toMatch(/^patterns\.portal-content\.[a-z-]+$/);
    }
  });
});
