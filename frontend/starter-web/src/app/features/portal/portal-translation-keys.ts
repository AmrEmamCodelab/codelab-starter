import { ContactTranslationKeys } from './contact/contact-translation-keys';
import { PortalContentTranslationKeys } from './content/portal-content-translation-keys';
import { FaqTranslationKeys } from './faq/faq-translation-keys';
import { LandingTranslationKeys } from './landing/landing-translation-keys';
import { PrivacyTranslationKeys, TermsTranslationKeys } from './legal/legal-translation-keys';
import { ServiceGuideTranslationKeys } from './service-guide/service-guide-translation-keys';

/** Texts every portal page shares (`portal.common.*`). */
export const PortalCommonTranslationKeys = {
  Home: 'portal.common.home',
  BreadcrumbLabel: 'portal.common.breadcrumb-label',
  LegalQuestions: 'portal.common.legal-questions',
  LegalContactLink: 'portal.common.legal-contact-link',
} as const;

/** Every `portal.*` key the feature renders, for the bundle check (`portal-translations.spec.ts`). */
export const PORTAL_TRANSLATION_KEYS: readonly string[] = [
  PortalCommonTranslationKeys,
  PortalContentTranslationKeys,
  LandingTranslationKeys,
  ServiceGuideTranslationKeys,
  FaqTranslationKeys,
  ContactTranslationKeys,
  PrivacyTranslationKeys,
  TermsTranslationKeys,
].flatMap((catalogue) => Object.values(catalogue) as string[]);
