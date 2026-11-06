import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import {
  SERVICE_REQUESTS_STAFF_TRANSLATION_KEYS,
  SERVICE_REQUEST_ACTION_LABEL_KEYS,
  SERVICE_REQUEST_STAFF_ADMIN_WORDING_KEYS,
} from './service-requests-staff-translation-keys';

const FOLDER = dirname(fileURLToPath(import.meta.url));
const SOURCE_FILE = /\.(ts|html)$/;
const NOT_SOURCE = /\.(spec|testing)\.ts$/;
/** A key literal of these screens, in single or double quotes. */
const KEY_LITERAL = /['"](serviceRequests\.(?:staff|record)(?:\.[a-z0-9-]+)+)['"]/g;

const DECLARED = [...SERVICE_REQUESTS_STAFF_TRANSLATION_KEYS, ...SERVICE_REQUEST_STAFF_ADMIN_WORDING_KEYS, ...SERVICE_REQUEST_ACTION_LABEL_KEYS];

describe('staff screen translations', () => {
  it('declares every key the screens\' sources name', () => {
    const named = readdirSync(FOLDER)
      .filter((name) => SOURCE_FILE.test(name) && !NOT_SOURCE.test(name))
      .flatMap((name) => [...readFileSync(join(FOLDER, name), 'utf8').matchAll(KEY_LITERAL)].map((match) => match[1]));
    expect(named.filter((key) => !DECLARED.includes(key))).toEqual([]);
  });

  it('keeps every key under the sample prefix', () => {
    expect(DECLARED.filter((key) => !key.startsWith('serviceRequests.'))).toEqual([]);
  });

  it.each([['ar', ar], ['en', en]])('has every staff text, staff/admin wording and action label in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(DECLARED.filter((key) => !flat[key])).toEqual([]);
  });
});
