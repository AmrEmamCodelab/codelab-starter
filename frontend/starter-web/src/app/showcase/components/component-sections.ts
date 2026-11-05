import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

/** The component categories, as the generated catalog groups them (catalog.schema.json `category`). */
export const ShowcaseCategory = {
  Layout: 'layout',
  Navigation: 'navigation',
  Content: 'content',
  Form: 'form',
  Data: 'data',
  Feedback: 'feedback',
  Media: 'media',
} as const;

export type ShowcaseCategory = (typeof ShowcaseCategory)[keyof typeof ShowcaseCategory];

/** One component section of the gallery: its selector (also the section's anchor id) and catalog category. */
export interface ComponentSection {
  readonly selector: string;
  readonly category: ShowcaseCategory;
}

/** The heading of each category on the gallery, in catalog order. */
export const CATEGORY_TITLES: Readonly<Record<ShowcaseCategory, string>> = {
  [ShowcaseCategory.Layout]: ShowcaseTranslationKeys.Categories.Layout,
  [ShowcaseCategory.Navigation]: ShowcaseTranslationKeys.Categories.Navigation,
  [ShowcaseCategory.Content]: ShowcaseTranslationKeys.Categories.Content,
  [ShowcaseCategory.Form]: ShowcaseTranslationKeys.Categories.Form,
  [ShowcaseCategory.Data]: ShowcaseTranslationKeys.Categories.Data,
  [ShowcaseCategory.Feedback]: ShowcaseTranslationKeys.Categories.Feedback,
  [ShowcaseCategory.Media]: ShowcaseTranslationKeys.Categories.Media,
};

/**
 * Every @codelab/ng-dga component the gallery shows. The registry spec fails when the package exports a component
 * (or the catalog lists a selector) without an entry here, and when an entry has no `#<selector>` section on the
 * page, so a new component cannot ship without its showcase section.
 */
export const COMPONENT_SECTIONS: readonly ComponentSection[] = [
  { selector: 'dga-card', category: ShowcaseCategory.Layout },
  { selector: 'dga-hero', category: ShowcaseCategory.Layout },
  { selector: 'dga-portal-page-header', category: ShowcaseCategory.Layout },
  { selector: 'dga-breadcrumb', category: ShowcaseCategory.Navigation },
  { selector: 'dga-header', category: ShowcaseCategory.Navigation },
  { selector: 'dga-stamp-strip', category: ShowcaseCategory.Navigation },
  { selector: 'dga-stepper', category: ShowcaseCategory.Navigation },
  { selector: 'dga-tabs', category: ShowcaseCategory.Navigation },
  { selector: 'dga-accordion', category: ShowcaseCategory.Content },
  { selector: 'dga-accordion-item', category: ShowcaseCategory.Content },
  { selector: 'dga-button', category: ShowcaseCategory.Form },
  { selector: 'dga-text-input', category: ShowcaseCategory.Form },
  { selector: 'dga-textarea', category: ShowcaseCategory.Form },
  { selector: 'dga-checkbox', category: ShowcaseCategory.Form },
  { selector: 'dga-radio-group', category: ShowcaseCategory.Form },
  { selector: 'dga-select', category: ShowcaseCategory.Form },
  { selector: 'dga-multi-select', category: ShowcaseCategory.Form },
  { selector: 'dga-date-input', category: ShowcaseCategory.Form },
  { selector: 'dga-hijri-date-input', category: ShowcaseCategory.Form },
  { selector: 'dga-unit-input', category: ShowcaseCategory.Form },
  { selector: 'dga-geo-select', category: ShowcaseCategory.Form },
  { selector: 'dga-file-upload', category: ShowcaseCategory.Form },
  { selector: 'dga-info-field', category: ShowcaseCategory.Data },
  { selector: 'dga-status-tag', category: ShowcaseCategory.Data },
  { selector: 'dga-tag', category: ShowcaseCategory.Data },
  { selector: 'dga-alert', category: ShowcaseCategory.Feedback },
  { selector: 'dga-confirm-dialog', category: ShowcaseCategory.Feedback },
  { selector: 'dga-error-dialog', category: ShowcaseCategory.Feedback },
  { selector: 'dga-page-feedback', category: ShowcaseCategory.Feedback },
  { selector: 'dga-hero-slider', category: ShowcaseCategory.Media },
  { selector: 'dga-icon', category: ShowcaseCategory.Media },
];

/** The categories in gallery order, each with its sections. */
export function componentCategories(
  sections: readonly ComponentSection[] = COMPONENT_SECTIONS,
): readonly { readonly category: ShowcaseCategory; readonly titleKey: string; readonly selectors: readonly string[] }[] {
  return Object.values(ShowcaseCategory).map(category => ({
    category,
    titleKey: CATEGORY_TITLES[category],
    selectors: sections.filter(section => section.category === category).map(section => section.selector),
  }));
}

/** The registry selectors missing from `expected` (a catalog or the package exports): an empty list means complete. */
export function missingSections(expected: Iterable<string>, sections: readonly ComponentSection[] = COMPONENT_SECTIONS): string[] {
  const known = new Set(sections.map(section => section.selector));
  return [...new Set(expected)].filter(selector => !known.has(selector)).sort();
}
