/**
 * The Umm al-Qura month-length table: one 12-bit mask per Hijri year from {@link UMM_AL_QURA_FIRST_YEAR} to
 * {@link UMM_AL_QURA_LAST_YEAR}. Bit `m` (0 = Muharram) is set when month `m + 1` has 30 days, clear when it has 29.
 *
 * The values are the month lengths of .NET's `System.Globalization.UmAlQuraCalendar` over its whole supported range
 * (1900-04-30 to 2077-11-16), so a date converted in the browser is the date a .NET backend converts. The browser's
 * own `islamic-umalqura` calendar (ICU) is used for month names only. Measured on Node 24's ICU, the two agree on every
 * day of 2000 to 2024 (the specs check it) but differ by a day on about 80% of dates before 2000, where dates of birth
 * fall, and on about a third of dates after 2030, so the browser calendar is not used for conversion.
 *
 * Data, not logic: never edit a value by hand. The masks must sum to 64,850 days (the spec checks it).
 */

/** The first Hijri year in the table. */
export const UMM_AL_QURA_FIRST_YEAR = 1318;

/** The last Hijri year in the table. */
export const UMM_AL_QURA_LAST_YEAR = 1500;

/** 1 Muharram 1318 as Gregorian numbers: 30 April 1900. */
export const UMM_AL_QURA_EPOCH = { year: 1900, month: 4, day: 30 } as const;

/** Hex digits per mask: each year's 12-bit mask is written as three hex digits. */
const MASK_HEX_DIGITS = 3;

/** The radix of the encoded masks. */
const HEX_RADIX = 16;

/** The masks as three upper-case hex digits per year, first year first (a string, so the data holds no numeric literals). */
const ENCODED_MONTH_MASKS =
  '2EA6E9ED2EA4D4AA96536AB5DAABA4B49A9352BA574B6AB55AAD55D2AA564AE95D2EC6D56AA5554AB95B2BA575BB27647496552AB55B' +
  'ADA6D4EC9D92D25A4D2AD56DB6AB52AA5A4B4979372B6575D6AD52A9692D25D4DDADA5D4DA9D52AAA4D69B63747697526A554BAAB55A' +
  'AD5DD2DA4D49A9552DA5D55AAD56AA69552BA574AE97656CB55AAAA554AD95D2DA5D9DB2BA4B4AA552B5575B6ABD2BC4B89A9552D5AD' +
  'B6A6D4DC9D92AA69562AE56D36AB55AAA94D49D95D2BA5B55AAD55A9A92E26E55DADA6D46A554BA9754EAAE5ACBA9D92B2564BCAB55A' +
  'B556D2EA5E4AA9552DAAD36C7596D269552DA5B4BA9BA3B4B69B52AA64B696D2EC6D9EB2D54D2AA564AE96DD6AB54B29A9352BA57536' +
  'AB56AAE93';

/** One month-length mask per year, first year first. */
export const UMM_AL_QURA_MONTH_MASKS: readonly number[] = Array.from(
  { length: ENCODED_MONTH_MASKS.length / MASK_HEX_DIGITS },
  (_unused, year) => Number.parseInt(ENCODED_MONTH_MASKS.slice(year * MASK_HEX_DIGITS, (year + 1) * MASK_HEX_DIGITS), HEX_RADIX),
);
