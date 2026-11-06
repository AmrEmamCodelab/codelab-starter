/**
 * The time zone the sample's dates are read in: the app (and Numbering) time zone, so "today" for the
 * preferred-date rule is the same calendar day on the server and in the browser, whatever the browser's own zone.
 */
export const SERVICE_REQUEST_TIME_ZONE = 'Asia/Riyadh';

const DATE_PARTS_LOCALE = 'en-CA';

type DatePart = 'year' | 'month' | 'day';

/**
 * The calendar date of `epochMilliseconds` in `timeZone`, as `YYYY-MM-DD`. Pass the injected `APP_CLOCK()` value
 * (never `Date.now()` directly) so specs control the day.
 */
export function todayInTimeZone(epochMilliseconds: number, timeZone: string = SERVICE_REQUEST_TIME_ZONE): string {
  const parts = new Intl.DateTimeFormat(DATE_PARTS_LOCALE, {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(new Date(epochMilliseconds));
  const part = (type: DatePart): string => parts.find((candidate) => candidate.type === type)?.value ?? '';
  return `${part('year')}-${part('month')}-${part('day')}`;
}
