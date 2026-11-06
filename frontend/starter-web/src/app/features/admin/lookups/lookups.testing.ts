import type { AdminLookupItem, LookupType } from './data/lookup.models';

/** Spec-only fixtures of the lookups screens (nothing in the app imports this file). */
export function lookupType(overrides: Partial<LookupType> = {}): LookupType {
  return { type: 'contact-topics', nameAr: 'مواضيع التواصل', nameEn: 'Contact topics', itemCount: 4, ...overrides };
}

export function lookupItem(overrides: Partial<AdminLookupItem> = {}): AdminLookupItem {
  return { id: 1, code: 'GENERAL', nameAr: 'استفسار عام', nameEn: 'General enquiry', sortOrder: 1, isActive: true, ...overrides };
}
