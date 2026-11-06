import type { PortalContentDocument, PortalContentSection } from './data/portal-content.models';
import type { DraftSection } from './section-draft';

/** Spec-only fixtures of the portal content editor (nothing in the app imports this file). */
export function contentSection(overrides: Partial<PortalContentSection> = {}): PortalContentSection {
  return {
    id: 'sec-1',
    titleAr: 'عن الخدمة',
    titleEn: 'About the service',
    bodyAr: 'فقرة أولى\n\nفقرة ثانية',
    bodyEn: 'First paragraph\n\nSecond paragraph',
    sortOrder: 1,
    ...overrides,
  };
}

export function contentDocument(overrides: Partial<PortalContentDocument> = {}): PortalContentDocument {
  return {
    page: 'faq',
    updatedAt: '2026-11-01T09:30:00Z',
    sections: [
      contentSection({ id: 'sec-2', titleEn: 'Second', sortOrder: 2 }),
      contentSection({ id: 'sec-1', titleEn: 'First', sortOrder: 1 }),
    ],
    ...overrides,
  };
}

export function draftSection(overrides: Partial<DraftSection> = {}): DraftSection {
  return { key: 1, id: 'sec-1', titleAr: 'عنوان', titleEn: 'Title', bodyAr: 'نص', bodyEn: 'Text', ...overrides };
}
