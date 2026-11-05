import type { DgaHeroSlide, DgaIconName } from '@codelab/ng-dga';
import { AppLanguages } from '../../../core/i18n/language';
import type { KpiTile, QuickAction } from '../../../patterns';
import { FAQ_CATEGORIES, faqEntryCount } from '../faq/faq-content';
import { PortalLinks } from '../portal-links';
import { GUIDE_STEPS } from '../service-guide/service-guide-content';
import { LandingTranslationKeys as Keys } from './landing-translation-keys';

/** One highlight of the landing slider, as translation keys and a portal link. */
export interface LandingSlide {
  readonly id: string;
  readonly titleKey: string;
  readonly subtitleKey: string;
  readonly linkLabelKey: string;
  readonly link: string;
}

/** One service card: what a portal page offers, with its link. */
export interface ServiceCard {
  readonly id: string;
  readonly icon: DgaIconName;
  readonly titleKey: string;
  readonly bodyKey: string;
  readonly linkLabelKey: string;
  readonly link: string;
}

export const LANDING_SLIDES: readonly LandingSlide[] = [
  {
    id: 'apply',
    titleKey: Keys.SlideApplyTitle,
    subtitleKey: Keys.SlideApplySubtitle,
    linkLabelKey: Keys.SlideApplyLink,
    link: PortalLinks.ServiceGuide,
  },
  {
    id: 'follow',
    titleKey: Keys.SlideFollowTitle,
    subtitleKey: Keys.SlideFollowSubtitle,
    linkLabelKey: Keys.SlideFollowLink,
    link: PortalLinks.ServiceGuide,
  },
  {
    id: 'help',
    titleKey: Keys.SlideHelpTitle,
    subtitleKey: Keys.SlideHelpSubtitle,
    linkLabelKey: Keys.SlideHelpLink,
    link: PortalLinks.Faq,
  },
];

export const SERVICE_CARDS: readonly ServiceCard[] = [
  {
    id: 'service-guide',
    icon: 'file',
    titleKey: Keys.ServiceGuideTitle,
    bodyKey: Keys.ServiceGuideBody,
    linkLabelKey: Keys.ServiceGuideLink,
    link: PortalLinks.ServiceGuide,
  },
  {
    id: 'faq',
    icon: 'question',
    titleKey: Keys.ServiceFaqTitle,
    bodyKey: Keys.ServiceFaqBody,
    linkLabelKey: Keys.ServiceFaqLink,
    link: PortalLinks.Faq,
  },
  {
    id: 'contact',
    icon: 'email',
    titleKey: Keys.ServiceContactTitle,
    bodyKey: Keys.ServiceContactBody,
    linkLabelKey: Keys.ServiceContactLink,
    link: PortalLinks.Contact,
  },
];

/**
 * The public tiles: figures that come from the portal's own content (steps of the guide, answered questions, languages),
 * so they never go stale. A project swaps in its own public figures from P5 settings.
 */
export const LANDING_TILES: readonly KpiTile[] = [
  { labelKey: Keys.TileSteps, value: GUIDE_STEPS.length, icon: 'check', link: PortalLinks.ServiceGuide },
  { labelKey: Keys.TileQuestions, value: faqEntryCount(FAQ_CATEGORIES), icon: 'question', link: PortalLinks.Faq },
  { labelKey: Keys.TileLanguages, value: Object.keys(AppLanguages).length, icon: 'language' },
];

export const LANDING_QUICK_ACTIONS: readonly QuickAction[] = [
  { labelKey: Keys.QuickLinkPrivacy, link: PortalLinks.Privacy, icon: 'info' },
  { labelKey: Keys.QuickLinkTerms, link: PortalLinks.Terms, icon: 'file' },
];

/** The slides as `dga-hero-slider` takes them, each text translated. */
export function heroSlides(slides: readonly LandingSlide[], translate: (key: string) => string): DgaHeroSlide[] {
  return slides.map(slide => ({
    id: slide.id,
    title: translate(slide.titleKey),
    subtitle: translate(slide.subtitleKey),
    link: slide.link,
    linkLabel: translate(slide.linkLabelKey),
  }));
}
