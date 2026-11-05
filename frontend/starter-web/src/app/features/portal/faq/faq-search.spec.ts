import { describe, expect, it } from 'vitest';
import { FAQ_CATEGORIES, faqEntryCount, type FaqCategory } from './faq-content';
import { filterFaq, normalizeSearchText, searchTerms } from './faq-search';

const CATEGORIES: readonly FaqCategory[] = [
  {
    id: 'general',
    titleKey: 'g',
    entries: [
      { id: 'one', questionKey: 'q1', answerKey: 'a1' },
      { id: 'two', questionKey: 'q2', answerKey: 'a2' },
    ],
  },
  { id: 'account', titleKey: 'acc', entries: [{ id: 'three', questionKey: 'q3', answerKey: 'a3' }] },
];

const TEXTS: Readonly<Record<string, string>> = {
  q1: 'How do I track my request?',
  a1: 'Open My requests and choose the request.',
  q2: 'كيف أُنشئ حساباً؟',
  a2: 'سجّل من صفحة الدخول.',
  q3: 'Forgot password',
  a3: 'Use the reset link on the sign-in page.',
};
const translate = (key: string): string => TEXTS[key] ?? key;

describe('normalizeSearchText', () => {
  it('lower-cases, trims and collapses whitespace', () => {
    expect(normalizeSearchText('  Track   MY\tRequest ')).toBe('track my request');
  });

  it('folds Arabic diacritics, tatweel and letter variants', () => {
    expect(normalizeSearchText('أُنشئ حساباً')).toBe('انشئ حسابا');
    expect(normalizeSearchText('إدارة')).toBe('اداره');
    expect(normalizeSearchText('مـــدى')).toBe('مدي');
    expect(normalizeSearchText('آخر')).toBe('اخر');
  });
});

describe('searchTerms', () => {
  it('has no words for an empty or blank query', () => {
    expect(searchTerms('')).toEqual([]);
    expect(searchTerms('   ')).toEqual([]);
  });

  it('splits the normalized query into words', () => {
    expect(searchTerms(' Track  Request ')).toEqual(['track', 'request']);
  });
});

describe('filterFaq', () => {
  it('returns the categories as given when there is no query', () => {
    expect(filterFaq(CATEGORIES, ' ', translate)).toBe(CATEGORIES);
  });

  it('keeps questions whose question or answer holds every word, in order', () => {
    const result = filterFaq(CATEGORIES, 'request open', translate);
    expect(result.map(category => category.id)).toEqual(['general']);
    expect(result[0]?.entries.map(entry => entry.id)).toEqual(['one']);
  });

  it('matches part of a word and the answer text', () => {
    expect(filterFaq(CATEGORIES, 'sign-in', translate)[0]?.entries.map(entry => entry.id)).toEqual(['three']);
    expect(filterFaq(CATEGORIES, 'pass', translate).map(category => category.id)).toEqual(['account']);
  });

  it('matches Arabic regardless of diacritics and hamza forms', () => {
    expect(filterFaq(CATEGORIES, 'انشئ حساب', translate)[0]?.entries.map(entry => entry.id)).toEqual(['two']);
  });

  it('drops a category with no match and returns nothing when nothing matches', () => {
    expect(filterFaq(CATEGORIES, 'track', translate).map(category => category.id)).toEqual(['general']);
    expect(filterFaq(CATEGORIES, 'nothing-like-this', translate)).toEqual([]);
  });

  it('leaves the input untouched', () => {
    filterFaq(CATEGORIES, 'track', translate);
    expect(CATEGORIES[0]?.entries).toHaveLength(2);
  });
});

describe('FAQ_CATEGORIES', () => {
  it('uses unique, anchor-safe ids for categories and entries', () => {
    const categoryIds = FAQ_CATEGORIES.map(category => category.id);
    const entryIds = FAQ_CATEGORIES.flatMap(category => category.entries.map(entry => entry.id));
    for (const id of [...categoryIds, ...entryIds]) {
      expect(id).toMatch(/^[a-z][a-z0-9-]*$/);
    }
    expect(new Set(categoryIds).size).toBe(categoryIds.length);
    expect(new Set(entryIds).size).toBe(entryIds.length);
  });

  it('counts its questions', () => {
    expect(faqEntryCount(FAQ_CATEGORIES)).toBe(8);
    expect(faqEntryCount([])).toBe(0);
  });
});
