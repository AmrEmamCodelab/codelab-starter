/**
 * Calendar-date text in ISO 8601 calendar form, `yyyy-mm-dd`: the one shape every date field of the library reads and
 * writes, and the shape an API sends. The same text form serves the Gregorian and the Umm al-Qura (Hijri) calendar;
 * which calendar a value belongs to is fixed by the field that holds it.
 *
 * Everything here is pure: no clock is read. "Today" always arrives from the caller (see {@link dgaTodayIso}).
 */

/** A calendar date as numbers; `month` is 1 to 12 and `day` 1 to 31, as written in the ISO text. */
export interface DgaCalendarDate {
  readonly year: number;
  readonly month: number;
  readonly day: number;
}

/** Why a date field's value cannot be used; the host maps each one to its own translated error text. */
export const DgaDateIssue = {
  /** The field holds text the browser or the parser cannot read as a date. */
  Unreadable: 'unreadable',
  /** Some parts of a multi-part date are filled and others are not. */
  Incomplete: 'incomplete',
  /** Every part is filled but the date does not exist (30th of a 29-day month) or is outside the calendar. */
  NotADate: 'not-a-date',
  /** The date is earlier than the effective minimum. */
  BeforeMin: 'before-min',
  /** The date is later than the effective maximum. */
  AfterMax: 'after-max',
} as const;
export type DgaDateIssue = (typeof DgaDateIssue)[keyof typeof DgaDateIssue];

/** The inclusive bounds a date must fall within; null means unbounded on that side. */
export interface DgaDateBounds {
  readonly min: string | null;
  readonly max: string | null;
}

/** What a field knows to work out its bounds: explicit limits plus, optionally, today and a today rule. */
export interface DgaDateLimits extends DgaDateBounds {
  /** Today as ISO text in the same calendar as min and max, or null when the caller gives no today. */
  readonly today: string | null;
  /** The value may not be later than today (a date of birth). */
  readonly notAfterToday: boolean;
  /** The value may not be earlier than today (an appointment). */
  readonly notBeforeToday: boolean;
}

/** The IANA time zone of Saudi Arabia (UTC+3, no daylight saving): the default zone for "today". */
export const DGA_KSA_TIME_ZONE = 'Asia/Riyadh';

const ISO_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
const YEAR_DIGITS = 4;
const MONTH_DAY_DIGITS = 2;
const PAD_CHARACTER = '0';
const SEPARATOR = '-';
const FIRST_MONTH = 1;
const LAST_MONTH = 12;
const FIRST_DAY = 1;
const MATCH_YEAR = 1;
const MATCH_MONTH = 2;
const MATCH_DAY = 3;
const UTC_NOON_HOUR = 12;
const JS_MONTH_OFFSET = 1;
/** A locale whose date parts are plain Latin digits, so formatToParts can be read as numbers in any app language. */
const NUMERIC_PARTS_LOCALE = 'en-CA';

/** Splits `yyyy-mm-dd` text into numbers without checking that the date exists; null when the shape is wrong. */
export function splitIsoDate(value: string | null | undefined): DgaCalendarDate | null {
  const match = ISO_DATE_PATTERN.exec((value ?? '').trim());
  if (match === null) return null;
  return { year: Number(match[MATCH_YEAR]), month: Number(match[MATCH_MONTH]), day: Number(match[MATCH_DAY]) };
}

/** The `yyyy-mm-dd` text of a date (zero-padded). */
export function formatIsoDate(date: DgaCalendarDate): string {
  return [
    String(date.year).padStart(YEAR_DIGITS, PAD_CHARACTER),
    String(date.month).padStart(MONTH_DAY_DIGITS, PAD_CHARACTER),
    String(date.day).padStart(MONTH_DAY_DIGITS, PAD_CHARACTER),
  ].join(SEPARATOR);
}

/**
 * Parses Gregorian `yyyy-mm-dd` text. Null for a wrong shape or a date that does not exist: 2026-02-30 is rejected,
 * never rolled into March, so a mistyped day surfaces as an error instead of silently becoming another date.
 */
export function parseGregorianIsoDate(value: string | null | undefined): DgaCalendarDate | null {
  const parts = splitIsoDate(value);
  if (parts === null || parts.month < FIRST_MONTH || parts.month > LAST_MONTH || parts.day < FIRST_DAY) return null;
  const instant = new Date(Date.UTC(parts.year, parts.month - JS_MONTH_OFFSET, parts.day, UTC_NOON_HOUR));
  const rolled =
    instant.getUTCFullYear() !== parts.year ||
    instant.getUTCMonth() !== parts.month - JS_MONTH_OFFSET ||
    instant.getUTCDate() !== parts.day;
  return rolled ? null : parts;
}

/** True when the text is a Gregorian date that exists. */
export function isGregorianIsoDate(value: string | null | undefined): boolean {
  return parseGregorianIsoDate(value) !== null;
}

/** The UTC-noon instant of a Gregorian date: a time of day that names the same date in every time zone. */
export function gregorianToInstant(date: DgaCalendarDate): Date {
  return new Date(Date.UTC(date.year, date.month - JS_MONTH_OFFSET, date.day, UTC_NOON_HOUR));
}

/** The Gregorian date (read in UTC) of an instant created by {@link gregorianToInstant}. */
export function instantToGregorian(instant: Date): DgaCalendarDate {
  return { year: instant.getUTCFullYear(), month: instant.getUTCMonth() + JS_MONTH_OFFSET, day: instant.getUTCDate() };
}

/**
 * Today's Gregorian date as `yyyy-mm-dd` in a time zone, from an instant the caller supplies (its clock, a test's
 * fixed time). The library never reads the clock itself, so a field's "today" rule is deterministic in tests.
 *
 * @example
 * readonly today = dgaTodayIso({ instant: new Date(clock.now()), timeZone: DGA_KSA_TIME_ZONE });
 */
export function dgaTodayIso(source: { readonly instant: Date; readonly timeZone: string }): string {
  const parts = new Intl.DateTimeFormat(NUMERIC_PARTS_LOCALE, {
    timeZone: source.timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(source.instant);
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((entry) => entry.type === type)?.value);
  return formatIsoDate({ year: part('year'), month: part('month'), day: part('day') });
}

/** The later of two optional ISO dates (null when both are null). */
function laterOf(first: string | null, second: string | null): string | null {
  if (first === null) return second;
  if (second === null) return first;
  return first >= second ? first : second;
}

/** The earlier of two optional ISO dates (null when both are null). */
function earlierOf(first: string | null, second: string | null): string | null {
  if (first === null) return second;
  if (second === null) return first;
  return first <= second ? first : second;
}

/**
 * The effective inclusive bounds: the explicit min and max, narrowed by the today rules when a today is given. A today
 * rule without a today is ignored rather than guessed. ISO text of one calendar compares correctly as plain strings.
 */
export function effectiveDateBounds(limits: DgaDateLimits): DgaDateBounds {
  const today = limits.today !== null && splitIsoDate(limits.today) !== null ? limits.today : null;
  return {
    min: laterOf(limits.min, limits.notBeforeToday ? today : null),
    max: earlierOf(limits.max, limits.notAfterToday ? today : null),
  };
}

/** BeforeMin or AfterMax when a non-empty ISO value is outside the bounds, otherwise null (empty is never out of range). */
export function dateRangeIssue(value: string, bounds: DgaDateBounds): DgaDateIssue | null {
  if (value === '') return null;
  if (bounds.min !== null && value < bounds.min) return DgaDateIssue.BeforeMin;
  if (bounds.max !== null && value > bounds.max) return DgaDateIssue.AfterMax;
  return null;
}
