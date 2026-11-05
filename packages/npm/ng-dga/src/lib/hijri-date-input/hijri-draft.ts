import { DgaDateIssue, formatIsoDate } from '../date-input/iso-date';
import { parseNumberText } from '../unit-input/number-text';
import { hijriMonthLength, isHijriDate, parseHijriIsoDate } from './umm-al-qura';

/**
 * What a segmented Hijri field holds while the person types: each part separately, possibly missing. The model value
 * is derived from it (a date only when all parts make a real date); the draft survives while the value is `''`, so a
 * half-filled field is never wiped.
 */
export interface HijriDraft {
  readonly year: number | null;
  readonly month: number | null;
  readonly day: number | null;
}

export const EMPTY_HIJRI_DRAFT: HijriDraft = { year: null, month: null, day: null };

/** The longest month, offered as day choices until the year and month fix the real length. */
const MAX_MONTH_DAYS = 30;
const FIRST_DAY = 1;

function isEmpty(draft: HijriDraft): boolean {
  return draft.year === null && draft.month === null && draft.day === null;
}

function isFilled(draft: HijriDraft): draft is { readonly year: number; readonly month: number; readonly day: number } {
  return draft.year !== null && draft.month !== null && draft.day !== null;
}

/** The draft of a Hijri ISO value; the empty draft for `''` or text that is not a Hijri date. */
export function hijriDraftFromIso(value: string): HijriDraft {
  return parseHijriIsoDate(value) ?? EMPTY_HIJRI_DRAFT;
}

/** The Hijri ISO value of a draft: a date only when every part is set and the date exists, otherwise `''`. */
export function hijriIsoFromDraft(draft: HijriDraft): string {
  return isFilled(draft) && isHijriDate(draft) ? formatIsoDate(draft) : '';
}

/** Incomplete when only some parts are set, NotADate when all are set but the date does not exist, else null. */
export function hijriDraftIssue(draft: HijriDraft): DgaDateIssue | null {
  if (isEmpty(draft)) return null;
  if (!isFilled(draft)) return DgaDateIssue.Incomplete;
  return isHijriDate(draft) ? null : DgaDateIssue.NotADate;
}

/**
 * The draft to show for a model value: the previous draft while it still stands for that value (the person's own
 * half-typed or impossible entry stands for `''`), otherwise the draft of the new value. This keeps typing state
 * across the model's echo and still follows a value set from outside.
 */
export function reconcileHijriDraft(value: string, previous: HijriDraft): HijriDraft {
  return hijriIsoFromDraft(previous) === value ? previous : hijriDraftFromIso(value);
}

/** The year typed in the year box: a whole number in either script, or null when empty or unreadable. */
export function hijriYearFromText(text: string): number | null {
  const year = parseNumberText(text, { integer: true });
  return year === undefined || Number.isNaN(year) ? null : year;
}

/** The day choices for the draft's year and month: 1..29 or 1..30, or 1..30 while either is unknown. */
export function hijriDayChoices(draft: HijriDraft): readonly number[] {
  const length = draft.year !== null && draft.month !== null ? hijriMonthLength({ year: draft.year, month: draft.month }) : null;
  return Array.from({ length: length ?? MAX_MONTH_DAYS }, (_unused, index) => index + FIRST_DAY);
}
