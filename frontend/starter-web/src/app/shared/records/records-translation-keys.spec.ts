import { describe, expect, it } from 'vitest';
import ar from '../../../assets/i18n/ar.json';
import en from '../../../assets/i18n/en.json';
import { flattenTranslations } from '../../core/i18n/translation.service';
import { AssignmentsErrorCodes, RecordsErrorCodes, WorkflowErrorCodes } from './records-error';
import { RECORDS_TRANSLATION_KEYS, TRANSITION_SIDE_KEYS } from './records-translation-keys';
import { TransitionSide } from './records.models';

const MUSTACHE = /\{\{\s*([\w.-]+)\s*\}\}/g;
/** The ng-dga textarea counter placeholders (single braces). */
const COUNTER = /\{(count|max|remaining)\}/g;
const ARABIC_LETTER = /[؀-ۿ]/;
const RECORD_ERROR_KEYS = [...Object.values(WorkflowErrorCodes), ...Object.values(RecordsErrorCodes), ...Object.values(AssignmentsErrorCodes)]
  .map((code) => `errors.${code}`);

function placeholders(text: string | undefined, pattern: RegExp): string[] {
  return [...(text ?? '').matchAll(pattern)].map((match) => match[1] ?? '').sort();
}

describe('records translations', () => {
  const bundles = { ar: flattenTranslations(ar), en: flattenTranslations(en) };

  it('labels every transition side', () => {
    expect(Object.keys(TRANSITION_SIDE_KEYS)).toHaveLength(Object.values(TransitionSide).filter((value) => typeof value === 'number').length);
  });

  it.each(['ar', 'en'] as const)('has every records.* key and every record refusal in the %s bundle', (language) => {
    expect([...RECORDS_TRANSLATION_KEYS, ...RECORD_ERROR_KEYS].filter((key) => !bundles[language][key])).toEqual([]);
  });

  it('holds no records.* key the generic UI does not render', () => {
    const known = new Set(RECORDS_TRANSLATION_KEYS);
    for (const bundle of [bundles.ar, bundles.en]) {
      expect(Object.keys(bundle).filter((key) => key.startsWith('records.') && !known.has(key))).toEqual([]);
    }
  });

  it('uses the same placeholders in both languages, and Arabic letters in every Arabic text', () => {
    for (const key of [...RECORDS_TRANSLATION_KEYS, ...RECORD_ERROR_KEYS]) {
      expect(placeholders(bundles.ar[key], MUSTACHE), key).toEqual(placeholders(bundles.en[key], MUSTACHE));
      expect(placeholders(bundles.ar[key], COUNTER), key).toEqual(placeholders(bundles.en[key], COUNTER));
      expect(bundles.ar[key], key).toMatch(ARABIC_LETTER);
    }
  });
});
