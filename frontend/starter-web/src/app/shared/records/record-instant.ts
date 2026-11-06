import type { AppLanguage } from '../../core/i18n/language';

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' };

/** Formats an instant for display in the app language. */
export type InstantFormatter = (iso: string) => string;

/** An ISO 8601 instant as local date and time text in `language`, or the raw value when it does not parse. */
export function formatRecordInstant(iso: string, language: AppLanguage): string {
  const instant = new Date(iso);
  return Number.isNaN(instant.getTime()) ? iso : new Intl.DateTimeFormat(language, INSTANT_FORMAT).format(instant);
}

/** Milliseconds since the epoch, or -Infinity for a value that does not parse (it sorts as the oldest). */
export function instantMs(iso: string): number {
  const time = Date.parse(iso);
  return Number.isNaN(time) ? Number.NEGATIVE_INFINITY : time;
}
