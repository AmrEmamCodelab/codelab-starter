import { describe, expect, it } from 'vitest';
import { formatIsoDate, gregorianToInstant } from '../date-input/iso-date';
import {
  DGA_HIJRI_YEAR_RANGE,
  gregorianIsoToHijriIso,
  gregorianToHijri,
  hijriIsoToGregorianIso,
  hijriMonthLength,
  hijriMonthNames,
  hijriToGregorian,
  isHijriDate,
  parseHijriIsoDate,
  UMM_AL_QURA_TOTAL_DAYS,
} from './umm-al-qura';
import { UMM_AL_QURA_MONTH_MASKS } from './umm-al-qura-table';

/** Published Umm al-Qura month starts (Gregorian), checked in both directions. */
const ANCHORS: readonly (readonly [hijri: string, gregorian: string])[] = [
  ['1318-01-01', '1900-04-30'],
  ['1420-01-01', '1999-04-17'],
  ['1445-09-01', '2024-03-11'],
  ['1446-01-01', '2024-07-07'],
  ['1447-09-01', '2026-02-18'],
];

const MS_PER_DAY = 86_400_000;

describe('the month table', () => {
  it('covers 1318 to 1500 and sums to the span 1900-04-30 .. 2077-11-16 inclusive', () => {
    expect(UMM_AL_QURA_MONTH_MASKS.length).toBe(DGA_HIJRI_YEAR_RANGE.last - DGA_HIJRI_YEAR_RANGE.first + 1);
    expect(UMM_AL_QURA_TOTAL_DAYS).toBe(64_850);
    expect(hijriIsoToGregorianIso('1500-12-' + String(hijriMonthLength({ year: 1500, month: 12 })))).toBe('2077-11-16');
  });

  it('gives 29 or 30 days, and null outside the table', () => {
    expect([29, 30]).toContain(hijriMonthLength({ year: 1446, month: 9 }));
    expect(hijriMonthLength({ year: 1317, month: 1 })).toBeNull();
    expect(hijriMonthLength({ year: 1446, month: 13 })).toBeNull();
    expect(hijriMonthLength({ year: 1446, month: 0 })).toBeNull();
  });
});

describe('Hijri to Gregorian and back', () => {
  it.each(ANCHORS)('%s is %s', (hijri, gregorian) => {
    expect(hijriIsoToGregorianIso(hijri)).toBe(gregorian);
    expect(gregorianIsoToHijriIso(gregorian)).toBe(hijri);
  });

  it('round-trips every 13th day across the whole table', () => {
    const start = gregorianToInstant({ year: 1900, month: 4, day: 30 }).getTime();
    for (let day = 0; day < UMM_AL_QURA_TOTAL_DAYS; day += 13) {
      const instant = new Date(start + day * MS_PER_DAY);
      const gregorian = { year: instant.getUTCFullYear(), month: instant.getUTCMonth() + 1, day: instant.getUTCDate() };
      const hijri = gregorianToHijri(gregorian);
      expect(hijri).not.toBeNull();
      expect(hijriToGregorian(hijri!)).toEqual(gregorian);
    }
  });

  it('moves one day at a time across a month boundary', () => {
    const length = hijriMonthLength({ year: 1446, month: 8 })!;
    const lastOfMonth = hijriIsoToGregorianIso(`1446-08-${length}`);
    const firstOfNext = hijriIsoToGregorianIso('1446-09-01');
    expect(Date.parse(firstOfNext) - Date.parse(lastOfMonth)).toBe(MS_PER_DAY);
  });

  it('rejects dates that do not exist instead of rolling them over', () => {
    const shortMonth = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12].find((month) => hijriMonthLength({ year: 1446, month }) === 29)!;
    expect(isHijriDate({ year: 1446, month: shortMonth, day: 30 })).toBe(false);
    expect(hijriToGregorian({ year: 1446, month: shortMonth, day: 30 })).toBeNull();
    expect(parseHijriIsoDate(`1446-${String(shortMonth).padStart(2, '0')}-30`)).toBeNull();
    expect(hijriIsoToGregorianIso('1446-13-01')).toBe('');
    expect(hijriIsoToGregorianIso('1446-1-1')).toBe('');
  });

  it('returns empty outside the table and for unreadable text', () => {
    expect(gregorianIsoToHijriIso('1900-04-29')).toBe('');
    expect(gregorianIsoToHijriIso('2077-11-17')).toBe('');
    expect(gregorianIsoToHijriIso('2026-02-30')).toBe('');
    expect(gregorianIsoToHijriIso(null)).toBe('');
    expect(hijriIsoToGregorianIso('1317-12-29')).toBe('');
  });
});

describe('agreement with the browser Umm al-Qura calendar', () => {
  const browser = new Intl.DateTimeFormat('en-u-nu-latn', {
    calendar: 'islamic-umalqura',
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    timeZone: 'UTC',
  });

  function browserHijri(gregorian: { year: number; month: number; day: number }): string {
    const parts = browser.formatToParts(gregorianToInstant(gregorian));
    const value = (type: string) => Number(parts.find((part) => part.type === type)?.value);
    return formatIsoDate({ year: value('year') || value('relatedYear'), month: value('month'), day: value('day') });
  }

  it('matches Intl islamic-umalqura on every day of 2000 to 2024', () => {
    const start = gregorianToInstant({ year: 2000, month: 1, day: 1 }).getTime();
    const end = gregorianToInstant({ year: 2024, month: 12, day: 31 }).getTime();
    for (let time = start; time <= end; time += MS_PER_DAY) {
      const instant = new Date(time);
      const gregorian = { year: instant.getUTCFullYear(), month: instant.getUTCMonth() + 1, day: instant.getUTCDate() };
      expect(formatIsoDate(gregorianToHijri(gregorian)!)).toBe(browserHijri(gregorian));
    }
  });
});

describe('hijriMonthNames', () => {
  it('names the twelve months in the requested language from the browser calendar', () => {
    const english = hijriMonthNames('en');
    const arabic = hijriMonthNames('ar');
    expect(english).toHaveLength(12);
    expect(english[0]).toMatch(/^Muharram/);
    expect(english[8]).toMatch(/^Ramadan/);
    expect(arabic[8]).toBe('رمضان');
    expect(new Set(arabic).size).toBe(12);
  });

  it('caches per locale', () => {
    expect(hijriMonthNames('en')).toBe(hijriMonthNames('en'));
  });
});
