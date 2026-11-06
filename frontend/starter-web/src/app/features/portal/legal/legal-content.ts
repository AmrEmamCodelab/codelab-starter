import { PortalContentPages, type PortalContentPage } from '../content/portal-content.client';
import { PrivacyTranslationKeys, TermsTranslationKeys } from './legal-translation-keys';

/**
 * A legal page (privacy policy, terms of use): its title and subtitle (translation keys, page chrome) and the content
 * page its sections come from (`GET api/portal/content/{page}`, edited by an administrator).
 */
export interface LegalDocument {
  readonly titleKey: string;
  readonly subtitleKey: string;
  readonly page: PortalContentPage;
}

export const PRIVACY_DOCUMENT: LegalDocument = {
  titleKey: PrivacyTranslationKeys.Title,
  subtitleKey: PrivacyTranslationKeys.Subtitle,
  page: PortalContentPages.Privacy,
};

export const TERMS_DOCUMENT: LegalDocument = {
  titleKey: TermsTranslationKeys.Title,
  subtitleKey: TermsTranslationKeys.Subtitle,
  page: PortalContentPages.Terms,
};
