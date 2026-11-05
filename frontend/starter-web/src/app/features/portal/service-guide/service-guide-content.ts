import type { PortalSection } from '../../../patterns';
import { ServiceGuideTranslationKeys as Keys } from './service-guide-translation-keys';

/** The section ids of the service guide (table-of-contents anchors `section-<id>`). */
export const ServiceGuideSectionIds = {
  Overview: 'overview',
  Eligibility: 'eligibility',
  Documents: 'documents',
  Steps: 'steps',
  Help: 'help',
} as const;

/** One step of the application, as translation keys. */
export interface GuideStep {
  readonly titleKey: string;
  readonly bodyKey: string;
}

/** The sections in page order, for the table of contents. */
export const SERVICE_GUIDE_SECTIONS: readonly PortalSection[] = [
  { id: ServiceGuideSectionIds.Overview, titleKey: Keys.OverviewTitle },
  { id: ServiceGuideSectionIds.Eligibility, titleKey: Keys.EligibilityTitle },
  { id: ServiceGuideSectionIds.Documents, titleKey: Keys.DocumentsTitle },
  { id: ServiceGuideSectionIds.Steps, titleKey: Keys.StepsTitle },
  { id: ServiceGuideSectionIds.Help, titleKey: Keys.HelpTitle },
];

export const OVERVIEW_PARAGRAPH_KEYS: readonly string[] = [Keys.OverviewBody1, Keys.OverviewBody2];

export const ELIGIBILITY_ITEM_KEYS: readonly string[] = [
  Keys.EligibilityItem1,
  Keys.EligibilityItem2,
  Keys.EligibilityItem3,
  Keys.EligibilityItem4,
];

export const DOCUMENT_ITEM_KEYS: readonly string[] = [
  Keys.DocumentsItem1,
  Keys.DocumentsItem2,
  Keys.DocumentsItem3,
  Keys.DocumentsItem4,
];

/** The application steps, in order. */
export const GUIDE_STEPS: readonly GuideStep[] = [
  { titleKey: Keys.Step1Title, bodyKey: Keys.Step1Body },
  { titleKey: Keys.Step2Title, bodyKey: Keys.Step2Body },
  { titleKey: Keys.Step3Title, bodyKey: Keys.Step3Body },
  { titleKey: Keys.Step4Title, bodyKey: Keys.Step4Body },
  { titleKey: Keys.Step5Title, bodyKey: Keys.Step5Body },
];

/** The 1-based number a step shows ("Step 2"), formatted for `language` (`ar`, `en`). */
export function stepNumber(index: number, language: string): string {
  return new Intl.NumberFormat(language).format(index + 1);
}
