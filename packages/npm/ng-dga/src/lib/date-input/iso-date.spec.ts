import { describe, expect, it } from 'vitest';
import {
  dateRangeIssue,
  dgaTodayIso,
  DGA_KSA_TIME_ZONE,
  DgaDateIssue,
  effectiveDateBounds,
  formatIsoDate,
  gregorianToInstant,
  instantToGregorian,
  isGregorianIsoDate,
  parseGregorianIsoDate,
  splitIsoDate,
} from './iso-date';

const NO_LIMITS = { min: null, max: null, today: null, notAfterToday: false, notBeforeToday: false };

describe('splitIsoDate and formatIsoDate', () => {
  it('round-trips zero-padded text', () => {
    expect(splitIsoDate('0987-03-05')).toEqual({ year: 987, month: 3, day: 5 });
    expect(formatIsoDate({ year: 987, month: 3, day: 5 })).toBe('0987-03-05');
  });

  it('rejects any other shape', () => {
    for (const text of ['', '2026-1-05', '05/03/2026', '2026-03-05T00:00', null, undefined]) expect(splitIsoDate(text)).toBeNull();
  });

  it('trims surrounding spaces', () => {
    expect(splitIsoDate(' 2026-03-05 ')).toEqual({ year: 2026, month: 3, day: 5 });
  });
});

describe('parseGregorianIsoDate', () => {
  it('accepts real dates, including 29 February of a leap year', () => {
    expect(parseGregorianIsoDate('2024-02-29')).toEqual({ year: 2024, month: 2, day: 29 });
    expect(isGregorianIsoDate('2026-12-31')).toBe(true);
  });

  it('rejects dates that would roll over instead of shifting them', () => {
    for (const text of ['2026-02-29', '2026-02-30', '2026-04-31', '2026-13-01', '2026-00-10', '2026-01-00']) {
      expect(parseGregorianIsoDate(text)).toBeNull();
    }
  });
});

describe('gregorianToInstant and instantToGregorian', () => {
  it('round-trip through a UTC-noon instant', () => {
    const date = { year: 1999, month: 12, day: 31 };
    const instant = gregorianToInstant(date);
    expect(instant.getUTCHours()).toBe(12);
    expect(instantToGregorian(instant)).toEqual(date);
  });
});

describe('dgaTodayIso', () => {
  it('reads the date in the given zone, not the machine zone', () => {
    // 22:30 UTC is already the next day in Riyadh (UTC+3).
    const instant = new Date(Date.UTC(2026, 10, 5, 22, 30));
    expect(dgaTodayIso({ instant, timeZone: DGA_KSA_TIME_ZONE })).toBe('2026-11-06');
    expect(dgaTodayIso({ instant, timeZone: 'UTC' })).toBe('2026-11-05');
  });
});

describe('effectiveDateBounds', () => {
  it('passes explicit bounds through when no today rule applies', () => {
    expect(effectiveDateBounds({ ...NO_LIMITS, min: '2020-01-01', max: '2030-01-01' })).toEqual({ min: '2020-01-01', max: '2030-01-01' });
  });

  it('caps the max at today for notAfterToday, keeping an earlier explicit max', () => {
    expect(effectiveDateBounds({ ...NO_LIMITS, today: '2026-11-05', notAfterToday: true })).toEqual({ min: null, max: '2026-11-05' });
    expect(effectiveDateBounds({ ...NO_LIMITS, max: '2026-01-01', today: '2026-11-05', notAfterToday: true }).max).toBe('2026-01-01');
  });

  it('raises the min to today for notBeforeToday, keeping a later explicit min', () => {
    expect(effectiveDateBounds({ ...NO_LIMITS, today: '2026-11-05', notBeforeToday: true }).min).toBe('2026-11-05');
    expect(effectiveDateBounds({ ...NO_LIMITS, min: '2027-01-01', today: '2026-11-05', notBeforeToday: true }).min).toBe('2027-01-01');
  });

  it('ignores a today rule without a usable today', () => {
    expect(effectiveDateBounds({ ...NO_LIMITS, notAfterToday: true })).toEqual({ min: null, max: null });
    expect(effectiveDateBounds({ ...NO_LIMITS, today: 'today', notAfterToday: true })).toEqual({ min: null, max: null });
  });
});

describe('dateRangeIssue', () => {
  const bounds = { min: '2026-01-01', max: '2026-12-31' };

  it('reports values outside the inclusive bounds', () => {
    expect(dateRangeIssue('2025-12-31', bounds)).toBe(DgaDateIssue.BeforeMin);
    expect(dateRangeIssue('2027-01-01', bounds)).toBe(DgaDateIssue.AfterMax);
  });

  it('accepts the bounds themselves and an empty value', () => {
    expect(dateRangeIssue('2026-01-01', bounds)).toBeNull();
    expect(dateRangeIssue('2026-12-31', bounds)).toBeNull();
    expect(dateRangeIssue('', bounds)).toBeNull();
    expect(dateRangeIssue('1900-01-01', { min: null, max: null })).toBeNull();
  });
});
