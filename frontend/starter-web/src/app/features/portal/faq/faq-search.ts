import type { FaqCategory } from './faq-content';

/** Arabic short vowels, tanween, shadda, sukun and the superscript alef: ignored when matching. */
const ARABIC_DIACRITICS = /[ً-ْٰ]/g;
/** The tatweel (kashida) stretch character: ignored when matching. */
const TATWEEL = /ـ/g;
/** Alef with hamza above or below, or with madda: matched as a bare alef. */
const ALEF_VARIANTS = /[أإآ]/g;
const BARE_ALEF = 'ا';
/** Ta marbuta matches ha, and alef maksura matches ya: readers type either. */
const TA_MARBUTA = /ة/g;
const HA = 'ه';
const ALEF_MAKSURA = /ى/g;
const YA = 'ي';
const WHITESPACE = /\s+/g;
const SPACE = ' ';

/**
 * Text as the FAQ search compares it: lower case, Arabic diacritics and tatweel removed, alef, ta marbuta and alef
 * maksura variants folded, runs of whitespace collapsed, ends trimmed.
 */
export function normalizeSearchText(text: string): string {
  return text
    .toLocaleLowerCase()
    .replace(ARABIC_DIACRITICS, '')
    .replace(TATWEEL, '')
    .replace(ALEF_VARIANTS, BARE_ALEF)
    .replace(TA_MARBUTA, HA)
    .replace(ALEF_MAKSURA, YA)
    .replace(WHITESPACE, SPACE)
    .trim();
}

/** The words of a query, normalized; an empty or blank query has none. */
export function searchTerms(query: string): string[] {
  const normalized = normalizeSearchText(query);
  return normalized === '' ? [] : normalized.split(SPACE);
}

/**
 * The categories with only the questions that match the query, in the same order; a category left with no question
 * is dropped. A question matches when every word of the query appears in its translated question or answer (any
 * order, part of a word is enough). No words means no filter: the categories come back as given.
 *
 * @param translate resolves a translation key in the active language.
 */
export function filterFaq(
  categories: readonly FaqCategory[],
  query: string,
  translate: (key: string) => string,
): readonly FaqCategory[] {
  const terms = searchTerms(query);
  if (terms.length === 0) {
    return categories;
  }
  return categories
    .map((category) => ({
      ...category,
      entries: category.entries.filter((entry) => {
        const text = normalizeSearchText(`${translate(entry.questionKey)} ${translate(entry.answerKey)}`);
        return terms.every((term) => text.includes(term));
      }),
    }))
    .filter((category) => category.entries.length > 0);
}
