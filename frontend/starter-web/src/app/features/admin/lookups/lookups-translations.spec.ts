import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';

const FOLDER = dirname(fileURLToPath(import.meta.url));
const SOURCE_FILE = /\.(ts|html)$/;
const NOT_SOURCE = /\.(spec|testing)\.ts$/;
/** A translation key literal the lookups screens render, in single or double quotes. */
const KEY_LITERAL = /['"](admin\.[a-z0-9-]+(?:\.[a-z0-9-]+)+)['"]/g;
/** The keys this folder adds (the admin home tile lives in admin-page, the rest are shared admin texts). */
const OWN_PREFIX = 'admin.lookups.';

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
  const keys = new Set<string>();
  for (const file of sourceFiles(FOLDER)) {
    for (const match of readFileSync(file, 'utf8').matchAll(KEY_LITERAL)) {
      keys.add(match[1]);
    }
  }
  return [...keys].sort();
}

describe('lookups translations', () => {
  it('finds the keys the lookups screens use', () => {
    expect(usedKeys().filter((key) => key.startsWith(OWN_PREFIX)).length).toBeGreaterThan(20);
  });

  it.each([['ar', ar], ['en', en]])('has every lookups key in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(usedKeys().filter((key) => !flat[key])).toEqual([]);
  });
});
