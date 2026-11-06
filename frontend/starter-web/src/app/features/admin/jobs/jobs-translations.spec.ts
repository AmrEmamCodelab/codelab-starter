import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import { JobsTranslationKeys } from './jobs-translation-keys';

const FOLDER = dirname(fileURLToPath(import.meta.url));
const SOURCE_FILE = /\.(ts|html)$/;
const NOT_SOURCE = /\.(spec|testing)\.ts$/;
/** A key literal of these screens, in single or double quotes. */
const KEY_LITERAL = /['"](admin\.jobs(?:\.[a-z0-9-]+)+)['"]/g;
/** The admin home's tile for these screens (`admin-page/admin-tiles.ts`). */
const TILE_KEYS = ['admin.home.jobs.title', 'admin.home.jobs.text'];

function leaves(node: unknown): string[] {
  return typeof node === 'string' ? [node] : Object.values(node as object).flatMap(leaves);
}

const DECLARED = leaves(JobsTranslationKeys);

describe('jobs translations', () => {
  it('declares every key the screens\' sources name', () => {
    const named = readdirSync(FOLDER)
      .filter((name) => SOURCE_FILE.test(name) && !NOT_SOURCE.test(name))
      .flatMap((name) => [...readFileSync(join(FOLDER, name), 'utf8').matchAll(KEY_LITERAL)].map((match) => match[1]));
    expect(named.filter((key) => !DECLARED.includes(key))).toEqual([]);
  });

  it.each([['ar', ar], ['en', en]])('has every jobs text in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect([...DECLARED, ...TILE_KEYS].filter((key) => !flat[key])).toEqual([]);
  });
});
