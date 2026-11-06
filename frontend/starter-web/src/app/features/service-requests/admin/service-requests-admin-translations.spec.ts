import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import { SERVICE_REQUESTS_ADMIN_TRANSLATION_KEYS } from './service-requests-admin-translation-keys';

const FOLDER = dirname(fileURLToPath(import.meta.url));
const SOURCE_FILE = /\.(ts|html)$/;
const NOT_SOURCE = /\.(spec|testing)\.ts$/;
/** A key literal of these screens, in single or double quotes. */
const KEY_LITERAL = /['"](serviceRequests\.admin(?:\.[a-z0-9-]+)+)['"]/g;

describe('admin oversight translations', () => {
  it('declares every key the screens\' sources name', () => {
    const named = readdirSync(FOLDER)
      .filter((name) => SOURCE_FILE.test(name) && !NOT_SOURCE.test(name))
      .flatMap((name) => [...readFileSync(join(FOLDER, name), 'utf8').matchAll(KEY_LITERAL)].map((match) => match[1]));
    expect(named.filter((key) => !SERVICE_REQUESTS_ADMIN_TRANSLATION_KEYS.includes(key))).toEqual([]);
  });

  it.each([['ar', ar], ['en', en]])('has every admin oversight text in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(SERVICE_REQUESTS_ADMIN_TRANSLATION_KEYS.filter((key) => !flat[key])).toEqual([]);
  });
});
