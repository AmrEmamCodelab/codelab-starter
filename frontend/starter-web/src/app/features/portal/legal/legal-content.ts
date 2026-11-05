import type { PortalSection } from '../../../patterns';
import { PrivacyTranslationKeys, TermsTranslationKeys } from './legal-translation-keys';

/**
 * One section of a legal document: a heading, its paragraphs, then an optional bullet list. Every value is a
 * translation key, so P5 can swap the static bundles for settings data without touching the page.
 */
export interface LegalSection {
  /** Stable slug: the table-of-contents anchor (`section-<id>`). */
  readonly id: string;
  readonly titleKey: string;
  readonly paragraphKeys: readonly string[];
  readonly itemKeys?: readonly string[];
}

/** A legal page (privacy policy, terms of use): its title, subtitle and sections, as translation keys. */
export interface LegalDocument {
  readonly titleKey: string;
  readonly subtitleKey: string;
  readonly sections: readonly LegalSection[];
}

export const PRIVACY_DOCUMENT: LegalDocument = {
  titleKey: PrivacyTranslationKeys.Title,
  subtitleKey: PrivacyTranslationKeys.Subtitle,
  sections: [
    {
      id: 'collect',
      titleKey: PrivacyTranslationKeys.CollectTitle,
      paragraphKeys: [PrivacyTranslationKeys.CollectBody],
      itemKeys: [PrivacyTranslationKeys.CollectItem1, PrivacyTranslationKeys.CollectItem2, PrivacyTranslationKeys.CollectItem3],
    },
    { id: 'use', titleKey: PrivacyTranslationKeys.UseTitle, paragraphKeys: [PrivacyTranslationKeys.UseBody] },
    { id: 'share', titleKey: PrivacyTranslationKeys.ShareTitle, paragraphKeys: [PrivacyTranslationKeys.ShareBody] },
    { id: 'keep', titleKey: PrivacyTranslationKeys.KeepTitle, paragraphKeys: [PrivacyTranslationKeys.KeepBody] },
    {
      id: 'rights',
      titleKey: PrivacyTranslationKeys.RightsTitle,
      paragraphKeys: [PrivacyTranslationKeys.RightsBody],
      itemKeys: [PrivacyTranslationKeys.RightsItem1, PrivacyTranslationKeys.RightsItem2, PrivacyTranslationKeys.RightsItem3],
    },
    { id: 'cookies', titleKey: PrivacyTranslationKeys.CookiesTitle, paragraphKeys: [PrivacyTranslationKeys.CookiesBody] },
  ],
};

export const TERMS_DOCUMENT: LegalDocument = {
  titleKey: TermsTranslationKeys.Title,
  subtitleKey: TermsTranslationKeys.Subtitle,
  sections: [
    { id: 'acceptance', titleKey: TermsTranslationKeys.AcceptanceTitle, paragraphKeys: [TermsTranslationKeys.AcceptanceBody] },
    { id: 'account', titleKey: TermsTranslationKeys.AccountTitle, paragraphKeys: [TermsTranslationKeys.AccountBody] },
    {
      id: 'acceptable-use',
      titleKey: TermsTranslationKeys.UseTitle,
      paragraphKeys: [TermsTranslationKeys.UseBody],
      itemKeys: [TermsTranslationKeys.UseItem1, TermsTranslationKeys.UseItem2, TermsTranslationKeys.UseItem3],
    },
    { id: 'content', titleKey: TermsTranslationKeys.ContentTitle, paragraphKeys: [TermsTranslationKeys.ContentBody] },
    { id: 'liability', titleKey: TermsTranslationKeys.LiabilityTitle, paragraphKeys: [TermsTranslationKeys.LiabilityBody] },
    { id: 'changes', titleKey: TermsTranslationKeys.ChangesTitle, paragraphKeys: [TermsTranslationKeys.ChangesBody] },
  ],
};

/** The document's sections as the portal content pattern's table of contents takes them. */
export function legalTocSections(document: LegalDocument): PortalSection[] {
  return document.sections.map((section) => ({ id: section.id, titleKey: section.titleKey }));
}
