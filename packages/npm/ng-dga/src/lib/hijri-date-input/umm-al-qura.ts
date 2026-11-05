import {
  type DgaCalendarDate,
  formatIsoDate,
  gregorianToInstant,
  instantToGregorian,
  parseGregorianIsoDate,
  splitIsoDate,
} from '../date-input/iso-date';
import { UMM_AL_QURA_EPOCH, UMM_AL_QURA_FIRST_YEAR, UMM_AL_QURA_LAST_YEAR, UMM_AL_QURA_MONTH_MASKS } from './umm-al-qura-table';

/**
 * Umm al-Qura (Hijri) calendar arithmetic over the month-length table: exact table look-ups, no search, drift or
 * rounding. Hijri dates use the same {@link DgaCalendarDate} numbers as Gregorian ones (month 1 = Muharram) and the
 * same `yyyy-mm-dd` text form.
 */

const MONTHS_PER_YEAR = 12;
const LONG_MONTH_DAYS = 30;
const SHORT_MONTH_DAYS = 29;
const FIRST_MONTH = 1;
const FIRST_DAY = 1;
const MS_PER_DAY = 86_400_000;
/** A day near the middle of every month, so a name read through another calendar engine never lands on a neighbour. */
const MID_MONTH_DAY = 15;
/** Any year in the table; month names do not depend on the year. */
const NAME_REFERENCE_YEAR = 1446;
const UMM_AL_QURA_CALENDAR = 'islamic-umalqura';
const NAME_TIME_ZONE = 'UTC';

/** The first and last Hijri years a date field accepts. */
export const DGA_HIJRI_YEAR_RANGE = { first: UMM_AL_QURA_FIRST_YEAR, last: UMM_AL_QURA_LAST_YEAR } as const;

const EPOCH_INSTANT = gregorianToInstant(UMM_AL_QURA_EPOCH).getTime();

function monthLength(mask: number, monthIndex: number): number {
  return (mask >> monthIndex) & 1 ? LONG_MONTH_DAYS : SHORT_MONTH_DAYS;
}

/** The day index (days since 1 Muharram 1318) on which each year starts, plus the total as the last entry. */
const YEAR_START_DAYS: readonly number[] = (() => {
  const starts: number[] = [];
  let total = 0;
  for (const mask of UMM_AL_QURA_MONTH_MASKS) {
    starts.push(total);
    for (let monthIndex = 0; monthIndex < MONTHS_PER_YEAR; monthIndex += 1) total += monthLength(mask, monthIndex);
  }
  starts.push(total);
  return starts;
})();

/** The number of days the table covers. */
export const UMM_AL_QURA_TOTAL_DAYS = YEAR_START_DAYS[YEAR_START_DAYS.length - 1];

function isYearInRange(year: number): boolean {
  return Number.isInteger(year) && year >= UMM_AL_QURA_FIRST_YEAR && year <= UMM_AL_QURA_LAST_YEAR;
}

function isMonthInRange(month: number): boolean {
  return Number.isInteger(month) && month >= FIRST_MONTH && month <= MONTHS_PER_YEAR;
}

/** Days in a Hijri month (29 or 30), or null when the year or month is outside the table. */
export function hijriMonthLength(yearMonth: { readonly year: number; readonly month: number }): number | null {
  if (!isYearInRange(yearMonth.year) || !isMonthInRange(yearMonth.month)) return null;
  return monthLength(UMM_AL_QURA_MONTH_MASKS[yearMonth.year - UMM_AL_QURA_FIRST_YEAR], yearMonth.month - FIRST_MONTH);
}

/** True when the Hijri date exists in the table: a 30th in a 29-day month does not. */
export function isHijriDate(date: DgaCalendarDate): boolean {
  const length = hijriMonthLength(date);
  return length !== null && Number.isInteger(date.day) && date.day >= FIRST_DAY && date.day <= length;
}

/** The Gregorian date of a Hijri date, or null when the Hijri date does not exist or is outside the table. */
export function hijriToGregorian(date: DgaCalendarDate): DgaCalendarDate | null {
  if (!isHijriDate(date)) return null;
  const yearIndex = date.year - UMM_AL_QURA_FIRST_YEAR;
  let dayIndex = YEAR_START_DAYS[yearIndex];
  for (let monthIndex = 0; monthIndex < date.month - FIRST_MONTH; monthIndex += 1) {
    dayIndex += monthLength(UMM_AL_QURA_MONTH_MASKS[yearIndex], monthIndex);
  }
  dayIndex += date.day - FIRST_DAY;
  return instantToGregorian(new Date(EPOCH_INSTANT + dayIndex * MS_PER_DAY));
}

/** The year index whose span holds the day index (binary search over the year starts). */
function yearIndexOf(dayIndex: number): number {
  let low = 0;
  let high = UMM_AL_QURA_MONTH_MASKS.length - 1;
  while (low < high) {
    const middle = (low + high + 1) >> 1;
    if (YEAR_START_DAYS[middle] <= dayIndex) low = middle;
    else high = middle - 1;
  }
  return low;
}

/** The Hijri date of a Gregorian date, or null when the Gregorian date is outside the table (1900-04-30 to 2077-11-16). */
export function gregorianToHijri(date: DgaCalendarDate): DgaCalendarDate | null {
  const dayIndex = Math.round((gregorianToInstant(date).getTime() - EPOCH_INSTANT) / MS_PER_DAY);
  if (dayIndex < 0 || dayIndex >= UMM_AL_QURA_TOTAL_DAYS) return null;
  const yearIndex = yearIndexOf(dayIndex);
  const mask = UMM_AL_QURA_MONTH_MASKS[yearIndex];
  let remaining = dayIndex - YEAR_START_DAYS[yearIndex];
  let monthIndex = 0;
  while (remaining >= monthLength(mask, monthIndex)) {
    remaining -= monthLength(mask, monthIndex);
    monthIndex += 1;
  }
  return { year: UMM_AL_QURA_FIRST_YEAR + yearIndex, month: monthIndex + FIRST_MONTH, day: remaining + FIRST_DAY };
}

/** Parses Hijri `yyyy-mm-dd` text; null for a wrong shape or a date that does not exist. */
export function parseHijriIsoDate(value: string | null | undefined): DgaCalendarDate | null {
  const parts = splitIsoDate(value);
  return parts !== null && isHijriDate(parts) ? parts : null;
}

/** The Hijri `yyyy-mm-dd` of a Gregorian `yyyy-mm-dd`, or `''` when it is unreadable or outside the table. */
export function gregorianIsoToHijriIso(value: string | null | undefined): string {
  const gregorian = parseGregorianIsoDate(value);
  const hijri = gregorian === null ? null : gregorianToHijri(gregorian);
  return hijri === null ? '' : formatIsoDate(hijri);
}

/** The Gregorian `yyyy-mm-dd` of a Hijri `yyyy-mm-dd`, or `''` when it is unreadable or does not exist. */
export function hijriIsoToGregorianIso(value: string | null | undefined): string {
  const hijri = parseHijriIsoDate(value);
  const gregorian = hijri === null ? null : hijriToGregorian(hijri);
  return gregorian === null ? '' : formatIsoDate(gregorian);
}

const monthNameCache = new Map<string, readonly string[]>();

/**
 * The twelve Hijri month names in a locale (`ar`, `en`, `ar-SA`), from the browser's Umm al-Qura calendar data, so the
 * library ships no text of its own. Cached per locale.
 */
export function hijriMonthNames(locale: string): readonly string[] {
  const cached = monthNameCache.get(locale);
  if (cached !== undefined) return cached;
  const format = new Intl.DateTimeFormat(locale, { calendar: UMM_AL_QURA_CALENDAR, month: 'long', timeZone: NAME_TIME_ZONE });
  const names = Array.from({ length: MONTHS_PER_YEAR }, (_unused, monthIndex) => {
    const gregorian = hijriToGregorian({ year: NAME_REFERENCE_YEAR, month: monthIndex + FIRST_MONTH, day: MID_MONTH_DAY });
    return gregorian === null ? '' : format.format(gregorianToInstant(gregorian));
  });
  monthNameCache.set(locale, names);
  return names;
}

