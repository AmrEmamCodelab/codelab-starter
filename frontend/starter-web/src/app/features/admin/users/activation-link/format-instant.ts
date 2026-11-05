import type { AppLanguage } from '../../../../core/i18n/language';

const INSTANT_FORMAT: Intl.DateTimeFormatOptions = { dateStyle: 'medium', timeStyle: 'short' };

/**
 * An ISO-8601 instant from the API (an activation link's expiry) as local date and time text in `language`, or the
 * raw value when it does not parse (never an "Invalid Date").
 */
export function formatInstant(iso: string, language: AppLanguage): string {
  const instant = new Date(iso);
  return Number.isNaN(instant.getTime()) ? iso : new Intl.DateTimeFormat(language, INSTANT_FORMAT).format(instant);
}
