import {
  DGA_ICON_GLYPHS,
  DgaFileUploadStatus,
  type DgaFileUploadItem,
  type DgaIconName,
  type DgaNavItem,
  type DgaStatusTone,
  type DgaTagTone,
} from '@codelab/ng-dga';
import type { TranslationParams } from '../../core/i18n/translation.service';
import { ShowcasePageId, showcaseLink } from '../showcase-pages';
import { ShowcaseTranslationKeys } from '../showcase-translation-keys';

/** Translates a key (the `TranslationService.translate` signature), so fixtures stay testable without Angular. */
export type Translate = (key: string, params?: TranslationParams) => string;

/** A fixture entry that names its visible text by translation key. */
export interface Labelled {
  readonly labelKey: string;
}

/** The entries with `labelKey` swapped for the translated `label`, ready for an ng-dga input. */
export function withLabels<T extends Labelled>(items: readonly T[], translate: Translate): (Omit<T, 'labelKey'> & { label: string })[] {
  return items.map(({ labelKey, ...rest }) => ({ ...rest, label: translate(labelKey) }));
}

/** A navigation fixture entry: a translated label, a link or children. */
export interface NavFixture extends Labelled {
  readonly id: string;
  readonly link?: string;
  readonly icon?: DgaIconName;
  readonly children?: readonly NavFixture[];
}

/** The nav fixture as `dga-header` items, translated at every level. */
export function navItems(items: readonly NavFixture[], translate: Translate): DgaNavItem[] {
  return items.map(({ labelKey, children, ...rest }) => ({
    ...rest,
    label: translate(labelKey),
    ...(children === undefined ? {} : { children: navItems(children, translate) }),
  }));
}

const Demo = ShowcaseTranslationKeys.Demo;
const galleryLink = showcaseLink(ShowcasePageId.Components);

export const BREADCRUMB_FIXTURE = [
  { labelKey: Demo.CrumbHome, link: galleryLink, icon: 'home' as DgaIconName },
  { labelKey: Demo.CrumbServices, link: showcaseLink(ShowcasePageId.PortalContent) },
  { labelKey: Demo.CrumbGuide },
] as const;

export const NAV_FIXTURE: readonly NavFixture[] = [
  { id: 'home', labelKey: Demo.NavHome, link: galleryLink },
  {
    id: 'services',
    labelKey: Demo.NavServices,
    children: [
      { id: 'guide', labelKey: Demo.NavGuide, link: showcaseLink(ShowcasePageId.PortalContent) },
      { id: 'faq', labelKey: Demo.NavFaq, link: showcaseLink(ShowcasePageId.PortalContent) },
    ],
  },
  { id: 'contact', labelKey: Demo.NavContact, link: showcaseLink(ShowcasePageId.Layouts) },
];

export const STEP_FIXTURE = [
  { labelKey: Demo.StepApplicant, completed: true },
  { labelKey: Demo.StepRequest },
  { labelKey: Demo.StepDocuments },
  { labelKey: Demo.StepReview },
] as const;

/** The step whose description shows in the vertical stepper. */
export const STEP_WITH_HINT = 1;

export const TAB_VALUES = { Open: 'open', Closed: 'closed', Archived: 'archived' } as const;

export const TAB_FIXTURE = [
  { value: TAB_VALUES.Open, labelKey: Demo.TabOpen, icon: 'file' as DgaIconName },
  { value: TAB_VALUES.Closed, labelKey: Demo.TabClosed, icon: 'check' as DgaIconName },
  { value: TAB_VALUES.Archived, labelKey: Demo.TabArchived, disabled: true },
] as const;

export const PRIORITY_VALUES = { Normal: 'normal', Urgent: 'urgent', Critical: 'critical' } as const;

export const PRIORITY_FIXTURE = [
  { value: PRIORITY_VALUES.Normal, labelKey: Demo.PriorityNormal },
  { value: PRIORITY_VALUES.Urgent, labelKey: Demo.PriorityUrgent },
  { value: PRIORITY_VALUES.Critical, labelKey: Demo.PriorityCritical, disabled: true },
] as const;

export const CITY_VALUES = { Riyadh: 'riyadh', Jeddah: 'jeddah', Dammam: 'dammam' } as const;

export const CITY_FIXTURE = [
  { value: CITY_VALUES.Riyadh, labelKey: Demo.CityRiyadh },
  { value: CITY_VALUES.Jeddah, labelKey: Demo.CityJeddah },
  { value: CITY_VALUES.Dammam, labelKey: Demo.CityDammam },
] as const;

export const LANGUAGE_VALUES = { Arabic: 'ar', English: 'en', French: 'fr', Urdu: 'ur' } as const;

export const LANGUAGE_FIXTURE = [
  { value: LANGUAGE_VALUES.Arabic, labelKey: Demo.LanguageArabic },
  { value: LANGUAGE_VALUES.English, labelKey: Demo.LanguageEnglish },
  { value: LANGUAGE_VALUES.French, labelKey: Demo.LanguageFrench },
  { value: LANGUAGE_VALUES.Urdu, labelKey: Demo.LanguageUrdu },
] as const;

export const STATUS_TONE_FIXTURE: readonly { readonly tone: DgaStatusTone; readonly labelKey: string }[] = [
  { tone: 'info', labelKey: Demo.StatusNew },
  { tone: 'progress', labelKey: Demo.StatusProcessing },
  { tone: 'warning', labelKey: Demo.StatusReturned },
  { tone: 'success', labelKey: Demo.StatusApproved },
  { tone: 'error', labelKey: Demo.StatusRejected },
  { tone: 'neutral', labelKey: Demo.StatusReview },
];

export const TAG_TONE_FIXTURE: readonly { readonly tone: DgaTagTone; readonly labelKey: string; readonly icon: DgaIconName | null }[] = [
  { tone: 'neutral', labelKey: Demo.StatusReview, icon: null },
  { tone: 'info', labelKey: Demo.StatusNew, icon: 'info' },
  { tone: 'success', labelKey: Demo.StatusApproved, icon: 'check' },
  { tone: 'warning', labelKey: Demo.StatusReturned, icon: 'warning' },
  { tone: 'error', labelKey: Demo.StatusRejected, icon: 'error' },
];

export const SLIDE_FIXTURE = [
  { id: 'services', titleKey: Demo.SlideServicesTitle, subtitleKey: Demo.SlideServicesSubtitle, link: showcaseLink(ShowcasePageId.PortalContent) },
  { id: 'track', titleKey: Demo.SlideTrackTitle, subtitleKey: Demo.SlideTrackSubtitle, link: showcaseLink(ShowcasePageId.RequesterDetail) },
  { id: 'help', titleKey: Demo.SlideHelpTitle, subtitleKey: Demo.SlideHelpSubtitle, link: showcaseLink(ShowcasePageId.Layouts) },
] as const;

/** Upload progress of the row that is still uploading (percent). */
export const UPLOAD_PROGRESS_PERCENT = 40;

/** The file-upload rows: one per status the component draws. File names are data, so they stay literal. */
export const UPLOAD_FIXTURE = [
  { id: 'id-copy', name: 'national-id.pdf', status: DgaFileUploadStatus.Uploaded, detailKey: Demo.UploadSize },
  { id: 'report', name: 'medical-report.pdf', status: DgaFileUploadStatus.Uploading, progress: UPLOAD_PROGRESS_PERCENT },
  { id: 'photo', name: 'photo.png', status: DgaFileUploadStatus.Failed, errorKey: Demo.UploadFailed },
] as const;

/** The id prefix of rows the upload demo adds for picked files. */
export const PICKED_ID_PREFIX = 'picked-';

/** The next free number for a picked row: one past the highest picked id so far (0 for the first pick). */
export function nextPickedNumber(items: readonly DgaFileUploadItem[]): number {
  const numbers = items
    .filter(item => item.id.startsWith(PICKED_ID_PREFIX))
    .map(item => Number(item.id.slice(PICKED_ID_PREFIX.length)))
    .filter(Number.isInteger);
  return numbers.length === 0 ? 0 : Math.max(...numbers) + 1;
}

/**
 * The upload rows after a pick: every accepted file is appended as a queued row (the demo has no server), with an id
 * that stays unique however many picks and removals follow.
 */
export function appendPicked(items: readonly DgaFileUploadItem[], accepted: readonly File[]): DgaFileUploadItem[] {
  const first = nextPickedNumber(items);
  const added = accepted.map((file, index) => ({
    id: `${PICKED_ID_PREFIX}${first + index}`,
    name: file.name,
    status: DgaFileUploadStatus.Queued,
  }));
  return [...items, ...added];
}

/** The upload rows without the one the user removed. */
export function removeItem(items: readonly DgaFileUploadItem[], id: string): DgaFileUploadItem[] {
  return items.filter(item => item.id !== id);
}

/** The largest file the upload demo accepts: 5 MB. */
export const UPLOAD_MAX_BYTES = 5_242_880;

/** How many files the upload demo accepts. */
export const UPLOAD_MAX_FILES = 5;

/** Every icon the package ships, in the font's order. */
export const ICON_NAMES = Object.keys(DGA_ICON_GLYPHS) as DgaIconName[];

/** The sample record number and phone the data demos show (data, not wording). */
export const SAMPLE_RECORD_NUMBER = 'REQ-2026-000142';
export const SAMPLE_PHONE = '05XXXXXXXX';

/** The weight the unit-input demo starts with, and the range it allows (kg). */
export const SAMPLE_WEIGHT_KG = 72.5;
export const WEIGHT_MIN_KG = 1;
export const WEIGHT_MAX_KG = 300;

/** The textarea counter limit of the notes demo (characters). */
export const NOTES_MAX_LENGTH = 200;

/** The Hijri date the Hijri demo starts with (Umm al-Qura ISO). */
export const SAMPLE_HIJRI_DATE = '1448-05-10';
