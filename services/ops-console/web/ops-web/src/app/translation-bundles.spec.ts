import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { CertificateKeys } from './shared/certificates/certificate-status';
import { AccessKeys } from './features/access/access.page';
import { JobKeys } from './features/jobs/job-status';
import { OverviewKeys } from './features/overview/health-status';
import { ReferenceKeys } from './features/reference/reference-code';
import { CommonKeys, ERROR_MESSAGE_KEYS } from './core/i18n/common-translation-keys';
import { flattenTranslations, type TranslationTree } from './core/i18n/translation.service';

const ASSETS = resolve(process.cwd(), 'src/assets/i18n');

function bundle(language: string): Record<string, string> {
  return flattenTranslations(JSON.parse(readFileSync(resolve(ASSETS, `${language}.json`), 'utf8')) as TranslationTree);
}

/** Every string leaf of a nested key-constant object. */
function keysOf(constants: object): string[] {
  return Object.values(constants).flatMap((value) => (typeof value === 'string' ? [value] : keysOf(value as object)));
}

const ARABIC = bundle('ar');
const ENGLISH = bundle('en');
const PLACEHOLDER = /\{\{\s*([\w.-]+)\s*\}\}/g;

describe('translation bundles', () => {
  it('ar and en carry exactly the same keys', () => {
    expect(Object.keys(ARABIC).sort()).toEqual(Object.keys(ENGLISH).sort());
  });

  it('no value is empty', () => {
    for (const [key, value] of [...Object.entries(ARABIC), ...Object.entries(ENGLISH)]) {
      expect(value.trim(), key).not.toBe('');
    }
  });

  it('ar and en use the same placeholders per key', () => {
    for (const key of Object.keys(ENGLISH)) {
      const names = (text: string) => [...text.matchAll(PLACEHOLDER)].map((match) => match[1]).sort();
      expect(names(ARABIC[key] ?? ''), key).toEqual(names(ENGLISH[key] ?? ''));
    }
  });

  it('every key the code uses exists in both bundles', () => {
    const used = [CommonKeys, OverviewKeys, ReferenceKeys, JobKeys, CertificateKeys, AccessKeys].flatMap(keysOf);
    for (const key of [...used, ...ERROR_MESSAGE_KEYS]) {
      expect(ARABIC[key], `ar: ${key}`).toBeDefined();
      expect(ENGLISH[key], `en: ${key}`).toBeDefined();
    }
  });
});
