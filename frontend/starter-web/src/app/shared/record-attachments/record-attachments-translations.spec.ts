import { describe, expect, it } from 'vitest';
import ar from '../../../assets/i18n/ar.json';
import en from '../../../assets/i18n/en.json';
import { flattenTranslations } from '../../core/i18n/translation.service';
import { AttachmentErrorCodes } from '../attachments/attachment-error-codes';
import { ATTACHMENT_ERROR_TEXT_KEYS, RECORD_ATTACHMENTS_TRANSLATION_KEYS, RecordAttachmentsTranslationKeys } from './record-attachments-keys';

const MUSTACHE = /\{\{\s*([\w.-]+)\s*\}\}/g;
/** The ng-dga `{name}` file-name placeholder. */
const NAME_PLACEHOLDER = '{name}';
const ARABIC_LETTER = /[؀-ۿ]/;

function placeholders(text: string | undefined): string[] {
  return [...(text ?? '').matchAll(MUSTACHE)].map((match) => match[1] ?? '').sort();
}

describe('record attachments translations', () => {
  const bundles = { ar: flattenTranslations(ar), en: flattenTranslations(en) };

  it('covers every service and module refusal code', () => {
    expect(ATTACHMENT_ERROR_TEXT_KEYS).toHaveLength(Object.values(AttachmentErrorCodes).length);
  });

  it.each(['ar', 'en'] as const)('has every widget and error key in the %s bundle', (language) => {
    expect(RECORD_ATTACHMENTS_TRANSLATION_KEYS.filter((key) => !bundles[language][key])).toEqual([]);
  });

  it('holds no attachments.widget key the widget does not render', () => {
    const known = new Set<string>(Object.values(RecordAttachmentsTranslationKeys));
    for (const bundle of [bundles.ar, bundles.en]) {
      expect(Object.keys(bundle).filter((key) => key.startsWith('attachments.widget.') && !known.has(key))).toEqual([]);
    }
  });

  it('uses the same placeholders in both languages, and Arabic letters in every Arabic text', () => {
    for (const key of RECORD_ATTACHMENTS_TRANSLATION_KEYS) {
      expect(placeholders(bundles.ar[key]), key).toEqual(placeholders(bundles.en[key]));
      expect(bundles.ar[key]?.includes(NAME_PLACEHOLDER), key).toBe(bundles.en[key]?.includes(NAME_PLACEHOLDER));
      expect(bundles.ar[key], key).toMatch(ARABIC_LETTER);
    }
  });
});
