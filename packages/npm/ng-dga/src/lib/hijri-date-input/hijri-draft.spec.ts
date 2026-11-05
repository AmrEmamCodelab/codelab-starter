import { describe, expect, it } from 'vitest';
import { DgaDateIssue } from '../date-input/iso-date';
import {
  EMPTY_HIJRI_DRAFT,
  hijriDayChoices,
  hijriDraftFromIso,
  hijriDraftIssue,
  hijriIsoFromDraft,
  hijriYearFromText,
  reconcileHijriDraft,
} from './hijri-draft';
import { hijriMonthLength } from './umm-al-qura';

const SHORT_MONTH = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12].find((month) => hijriMonthLength({ year: 1446, month }) === 29)!;

describe('hijriDraftFromIso and hijriIsoFromDraft', () => {
  it('round-trip a real date', () => {
    const draft = hijriDraftFromIso('1446-09-15');
    expect(draft).toEqual({ year: 1446, month: 9, day: 15 });
    expect(hijriIsoFromDraft(draft)).toBe('1446-09-15');
  });

  it('gives the empty draft for empty or non-Hijri text', () => {
    expect(hijriDraftFromIso('')).toEqual(EMPTY_HIJRI_DRAFT);
    expect(hijriDraftFromIso('1200-01-01')).toEqual(EMPTY_HIJRI_DRAFT);
  });

  it('gives no value for a partial or impossible draft', () => {
    expect(hijriIsoFromDraft({ year: 1446, month: 9, day: null })).toBe('');
    expect(hijriIsoFromDraft({ year: 1446, month: SHORT_MONTH, day: 30 })).toBe('');
  });
});

describe('hijriDraftIssue', () => {
  it('names incomplete and impossible drafts and accepts empty and real ones', () => {
    expect(hijriDraftIssue(EMPTY_HIJRI_DRAFT)).toBeNull();
    expect(hijriDraftIssue({ year: 1446, month: null, day: 3 })).toBe(DgaDateIssue.Incomplete);
    expect(hijriDraftIssue({ year: 1446, month: SHORT_MONTH, day: 30 })).toBe(DgaDateIssue.NotADate);
    expect(hijriDraftIssue({ year: 1200, month: 1, day: 1 })).toBe(DgaDateIssue.NotADate);
    expect(hijriDraftIssue({ year: 1446, month: 1, day: 1 })).toBeNull();
  });
});

describe('reconcileHijriDraft', () => {
  it('keeps a half-typed draft across the empty echo', () => {
    const partial = { year: 1446, month: 2, day: null };
    expect(reconcileHijriDraft('', partial)).toBe(partial);
  });

  it('keeps the draft that produced the value', () => {
    const full = { year: 1446, month: 2, day: 5 };
    expect(reconcileHijriDraft('1446-02-05', full)).toBe(full);
  });

  it('follows a value set from outside', () => {
    expect(reconcileHijriDraft('1410-06-15', { year: 1446, month: 2, day: null })).toEqual({ year: 1410, month: 6, day: 15 });
    expect(reconcileHijriDraft('', { year: 1446, month: 2, day: 5 })).toEqual(EMPTY_HIJRI_DRAFT);
  });
});

describe('hijriYearFromText', () => {
  it('reads whole years in either script', () => {
    expect(hijriYearFromText('1446')).toBe(1446);
    expect(hijriYearFromText('١٤٤٦')).toBe(1446);
    expect(hijriYearFromText('')).toBeNull();
    expect(hijriYearFromText('14a6')).toBeNull();
    expect(hijriYearFromText('1446.5')).toBeNull();
  });
});

describe('hijriDayChoices', () => {
  it('offers 30 days until the month is known, then the real length', () => {
    expect(hijriDayChoices(EMPTY_HIJRI_DRAFT)).toHaveLength(30);
    expect(hijriDayChoices({ year: 1446, month: SHORT_MONTH, day: null })).toHaveLength(29);
    expect(hijriDayChoices({ year: 1446, month: SHORT_MONTH, day: null })[0]).toBe(1);
  });
});
