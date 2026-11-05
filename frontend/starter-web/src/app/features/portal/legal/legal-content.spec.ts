import { describe, expect, it } from 'vitest';
import { SECTION_ID_PATTERN } from '../../../patterns';
import { PRIVACY_DOCUMENT, TERMS_DOCUMENT, legalTocSections } from './legal-content';

describe.each([
  ['privacy', PRIVACY_DOCUMENT, 'portal.privacy.'],
  ['terms', TERMS_DOCUMENT, 'portal.terms.'],
])('the %s document', (_name, document, prefix) => {
  it('has unique, anchor-safe section ids', () => {
    const ids = document.sections.map(section => section.id);
    for (const id of ids) {
      expect(id).toMatch(SECTION_ID_PATTERN);
    }
    expect(new Set(ids).size).toBe(ids.length);
  });

  it('keeps every key in its own namespace and every section non-empty', () => {
    const keys = [
      document.titleKey,
      document.subtitleKey,
      ...document.sections.flatMap(section => [section.titleKey, ...section.paragraphKeys, ...(section.itemKeys ?? [])]),
    ];
    for (const key of keys) {
      expect(key.startsWith(prefix)).toBe(true);
    }
    for (const section of document.sections) {
      expect(section.paragraphKeys.length).toBeGreaterThan(0);
    }
  });

  it('lists its sections for the table of contents', () => {
    expect(legalTocSections(document)).toEqual(document.sections.map(section => ({ id: section.id, titleKey: section.titleKey })));
  });
});
