import { Pipe, inject, type PipeTransform } from '@angular/core';
import { AppLanguages, type AppLanguage } from './language';
import { TranslationService } from './translation.service';

/**
 * The Intl locale per language. Operators compare times with server logs, so both languages use the Gregorian
 * calendar and Latin digits (an Arabic locale would otherwise default to another calendar or numbering system).
 */
export const DATE_TIME_LOCALES: Readonly<Record<AppLanguage, string>> = Object.freeze({
  [AppLanguages.Arabic]: 'ar-u-ca-gregory-nu-latn',
  [AppLanguages.English]: 'en-GB',
});

const DATE_TIME_OPTIONS: Intl.DateTimeFormatOptions = Object.freeze({
  dateStyle: 'medium',
  timeStyle: 'medium',
});

/**
 * An ISO 8601 timestamp as local date and time text for `language`. Null, undefined, empty and unparsable values give
 * an empty string, so a cell never shows "Invalid Date".
 */
export function formatDateTime(value: string | null | undefined, language: AppLanguage): string {
  if (!value) {
    return '';
  }
  const instant = new Date(value);
  if (Number.isNaN(instant.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(DATE_TIME_LOCALES[language], DATE_TIME_OPTIONS).format(instant);
}

/** `{{ row.notAfter | dateTime }}`: {@link formatDateTime} in the active language (impure: follows the language). */
@Pipe({ name: 'dateTime', pure: false })
export class DateTimePipe implements PipeTransform {
  private readonly translations = inject(TranslationService);

  transform(value: string | null | undefined): string {
    return formatDateTime(value, this.translations.language());
  }
}
