import { describe, expect, it } from 'vitest';
import type { LocalizedSection } from '../content/portal-content';
import { FAQ_CATEGORIES, faqEntryCount } from './faq-content';
import { filterFaq, normalizeSearchText, searchTerms } from './faq-search';

const QUESTIONS: readonly LocalizedSection[] = [
  { id: 'one', anchor: 'part-1', title: 'How do I track my request?', paragraphs: ['Open My requests', 'and choose the request.'] },
  { id: 'two', anchor: 'part-2', title: 'كيف أُنشئ حساباً؟', paragraphs: ['سجّل من صفحة الدخول.'] },
  { id: 'three', anchor: 'part-3', title: 'Forgot password', paragraphs: ['Use the reset link on the sign-in page.'] },
];

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
  const ids = (questions: readonly LocalizedSection[]): string[] => questions.map(question => question.id);

  it('returns the questions as given when there is no query', () => {
    expect(filterFaq(QUESTIONS, ' ')).toBe(QUESTIONS);
  });

  it('keeps questions whose question or answer holds every word, in order', () => {
    expect(ids(filterFaq(QUESTIONS, 'request open'))).toEqual(['one']);
  });

  it('matches across answer paragraphs, part of a word and the answer text', () => {
    expect(ids(filterFaq(QUESTIONS, 'requests choose'))).toEqual(['one']);
    expect(ids(filterFaq(QUESTIONS, 'sign-in'))).toEqual(['three']);
    expect(ids(filterFaq(QUESTIONS, 'pass'))).toEqual(['three']);
  });

  it('matches Arabic regardless of diacritics and hamza forms', () => {
    expect(ids(filterFaq(QUESTIONS, 'انشئ حساب'))).toEqual(['two']);
  });

  it('returns nothing when nothing matches and leaves the input untouched', () => {
    expect(filterFaq(QUESTIONS, 'nothing-like-this')).toEqual([]);
    expect(QUESTIONS).toHaveLength(3);
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
