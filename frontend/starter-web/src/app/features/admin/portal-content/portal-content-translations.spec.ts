import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import { ValidationMessageCodes } from '../organizations/data/admin-api-error';
import { PORTAL_CONTENT_PAGES, PortalContentPageLabelKeys } from './data/portal-content.models';

const FOLDER = dirname(fileURLToPath(import.meta.url));
const SOURCE_FILE = /\.(ts|html)$/;
const NOT_SOURCE = /\.(spec|testing)\.ts$/;
/** A translation key literal the editor renders, in single or double quotes. */
const KEY_LITERAL = /['"](admin\.[a-z0-9-]+(?:\.[a-z0-9-]+)+)['"]/g;
const OWN_PREFIX = 'admin.portal-content.';
/** The client checks' messages (`sectionErrors`). */
const CHECK_KEYS = [ValidationMessageCodes.Required, ValidationMessageCodes.TooLong].map((code) => `errors.${code}`);

function sourceFiles(folder: string): string[] {
  return readdirSync(folder).flatMap((name) => {
    const path = join(folder, name);
    if (statSync(path).isDirectory()) {
      return sourceFiles(path);
    }
    return SOURCE_FILE.test(name) && !NOT_SOURCE.test(name) ? [path] : [];
  });
}

function usedKeys(): string[] {
  const keys = new Set<string>(CHECK_KEYS);
  for (const file of sourceFiles(FOLDER)) {
    for (const match of readFileSync(file, 'utf8').matchAll(KEY_LITERAL)) {
      keys.add(match[1]);
    }
  }
  return [...keys].sort();
}

describe('portal content translations', () => {
  it('finds the keys the editor uses, a label for every page included', () => {
    const keys = usedKeys();
    expect(keys.filter((key) => key.startsWith(OWN_PREFIX)).length).toBeGreaterThan(30);
    expect(PORTAL_CONTENT_PAGES.every((page) => keys.includes(PortalContentPageLabelKeys[page]))).toBe(true);
  });

  it.each([['ar', ar], ['en', en]])('has every portal content key in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(usedKeys().filter((key) => !flat[key])).toEqual([]);
  });
});
