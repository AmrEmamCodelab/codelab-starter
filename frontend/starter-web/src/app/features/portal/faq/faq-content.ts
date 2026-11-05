import { FaqTranslationKeys as Keys } from './faq-translation-keys';

/** One question and its answer, as translation keys. */
export interface FaqEntry {
  /** Stable slug, unique across the page (tracking). */
  readonly id: string;
  readonly questionKey: string;
  readonly answerKey: string;
}

/** A group of questions: one section of the page and one table-of-contents entry. */
export interface FaqCategory {
  /** Stable slug: the table-of-contents anchor (`section-<id>`). */
  readonly id: string;
  readonly titleKey: string;
  readonly entries: readonly FaqEntry[];
}

/** The questions in page order. P5 replaces this static list with settings data of the same shape. */
export const FAQ_CATEGORIES: readonly FaqCategory[] = [
  {
    id: 'general',
    titleKey: Keys.GeneralTitle,
    entries: [
      { id: 'about', questionKey: Keys.AboutQuestion, answerKey: Keys.AboutAnswer },
      { id: 'fee', questionKey: Keys.FeeQuestion, answerKey: Keys.FeeAnswer },
      { id: 'language', questionKey: Keys.LanguageQuestion, answerKey: Keys.LanguageAnswer },
    ],
  },
  {
    id: 'account',
    titleKey: Keys.AccountTitle,
    entries: [
      { id: 'get-account', questionKey: Keys.AccountQuestion, answerKey: Keys.AccountAnswer },
      { id: 'password', questionKey: Keys.PasswordQuestion, answerKey: Keys.PasswordAnswer },
    ],
  },
  {
    id: 'requests',
    titleKey: Keys.RequestsTitle,
    entries: [
      { id: 'track', questionKey: Keys.TrackQuestion, answerKey: Keys.TrackAnswer },
      { id: 'returned', questionKey: Keys.ReturnedQuestion, answerKey: Keys.ReturnedAnswer },
      { id: 'duration', questionKey: Keys.DurationQuestion, answerKey: Keys.DurationAnswer },
    ],
  },
];

/** How many questions the categories hold. */
export function faqEntryCount(categories: readonly FaqCategory[]): number {
  return categories.reduce((count, category) => count + category.entries.length, 0);
}
