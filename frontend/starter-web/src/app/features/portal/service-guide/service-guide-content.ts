import { ServiceGuideTranslationKeys as Keys } from './service-guide-translation-keys';

/** The section id of the guide's closing help block (table-of-contents anchor `section-help`). */
export const ServiceGuideSectionIds = {
  Help: 'help',
} as const;

/** One step of the application, as translation keys. */
export interface GuideStep {
  readonly titleKey: string;
  readonly bodyKey: string;
}

/**
 * The application steps of the static P4 guide. The guide page reads its sections from the API now
 * (`GET api/portal/content/service-guide`); this list only feeds the landing page's "steps" tile.
 */
export const GUIDE_STEPS: readonly GuideStep[] = [
  { titleKey: Keys.Step1Title, bodyKey: Keys.Step1Body },
  { titleKey: Keys.Step2Title, bodyKey: Keys.Step2Body },
  { titleKey: Keys.Step3Title, bodyKey: Keys.Step3Body },
  { titleKey: Keys.Step4Title, bodyKey: Keys.Step4Body },
  { titleKey: Keys.Step5Title, bodyKey: Keys.Step5Body },
];
