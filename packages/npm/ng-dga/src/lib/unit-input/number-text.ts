/**
 * Reading numbers that people type, in Arabic or English: Arabic-Indic (٠-٩) and Extended Arabic-Indic (۰-۹) digits
 * and the Arabic decimal separator (٫) are accepted beside Latin digits and '.'. Values are always held as numbers
 * and written back with Latin digits, the form a field shows and an API sends.
 */

/** Why a number field's value cannot be used; the host maps each one to its own translated error text. */
export const DgaNumberIssue = {
  /** The text is not a number. */
  Unreadable: 'unreadable',
  /** The number is smaller than the minimum. */
  BelowMin: 'below-min',
  /** The number is larger than the maximum. */
  AboveMax: 'above-max',
} as const;
export type DgaNumberIssue = (typeof DgaNumberIssue)[keyof typeof DgaNumberIssue];

/** The inclusive range a number must fall within; null means unbounded on that side. */
export interface DgaNumberRange {
  readonly min: number | null;
  readonly max: number | null;
}

const ARABIC_INDIC_ZERO = 0x0660;
const EXTENDED_ARABIC_INDIC_ZERO = 0x06f0;
const DIGIT_COUNT = 10;
const LATIN_ZERO = 0x0030;
const ARABIC_DECIMAL_SEPARATOR = '٫';
const DECIMAL_POINT = '.';
const ARABIC_DIGITS = /[٠-٩۰-۹]/g;
/** An optional minus, digits, and at most one decimal point with digits on at least one side of it. */
const DECIMAL_PATTERN = /^-?(\d+\.?\d*|\.\d+)$/;
const INTEGER_PATTERN = /^-?\d+$/;

function latinDigit(character: string): string {
  const code = character.charCodeAt(0);
  const zero = code >= EXTENDED_ARABIC_INDIC_ZERO && code < EXTENDED_ARABIC_INDIC_ZERO + DIGIT_COUNT ? EXTENDED_ARABIC_INDIC_ZERO : ARABIC_INDIC_ZERO;
  return String.fromCharCode(LATIN_ZERO + code - zero);
}

/** The text with Arabic-Indic digits and the Arabic decimal separator replaced by their Latin forms, trimmed. */
export function normalizeDigits(text: string): string {
  return text.trim().replace(ARABIC_DIGITS, latinDigit).split(ARABIC_DECIMAL_SEPARATOR).join(DECIMAL_POINT);
}

/**
 * The number typed in a field: undefined for empty text, NaN for text that is not a number, otherwise the number.
 * With `integer`, a decimal point makes the text unreadable.
 */
export function parseNumberText(text: string, options: { readonly integer: boolean }): number | undefined {
  const normalized = normalizeDigits(text);
  if (normalized === '') return undefined;
  const pattern = options.integer ? INTEGER_PATTERN : DECIMAL_PATTERN;
  return pattern.test(normalized) ? Number(normalized) : Number.NaN;
}

/** The text a field shows for a number: Latin digits, '' for null. */
export function formatNumberText(value: number | null): string {
  return value === null ? '' : String(value);
}

/** BelowMin or AboveMax when the number is outside the inclusive range, otherwise null (null is never out of range). */
export function numberRangeIssue(value: number | null, range: DgaNumberRange): DgaNumberIssue | null {
  if (value === null) return null;
  if (range.min !== null && value < range.min) return DgaNumberIssue.BelowMin;
  if (range.max !== null && value > range.max) return DgaNumberIssue.AboveMax;
  return null;
}
