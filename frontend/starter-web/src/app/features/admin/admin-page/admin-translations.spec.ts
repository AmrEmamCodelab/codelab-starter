import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import {
  IdentityAccountErrorCodes,
  UsersErrorCodes,
  UsersImportRowErrorCodes,
  ValidationMessageCodes,
  errorCodeKey,
} from '../organizations/data/admin-api-error';

const ADMIN_DIR = join(dirname(fileURLToPath(import.meta.url)), '..');
/** The areas this spec covers (the access area checks its own keys). */
const AREAS = ['admin-page', 'users', 'organizations', 'import'];
const SOURCE_FILE = /\.(ts|html)$/;
const NOT_SOURCE = /\.(spec|testing)\.ts$/;
/** A translation key literal of the admin area, in single or double quotes. */
const KEY_LITERAL = /['"](admin\.(?:title|home|users|organizations|import|common|activation)(?:\.[a-z0-9-]+)*)['"]/g;

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
  for (const file of AREAS.flatMap((area) => sourceFiles(join(ADMIN_DIR, area)))) {
    for (const match of readFileSync(file, 'utf8').matchAll(KEY_LITERAL)) {
      keys.add(match[1]);
    }
  }
  return [...keys].sort();
}

const ERROR_KEYS = [
  ...Object.values(UsersErrorCodes),
  ...Object.values(UsersImportRowErrorCodes),
  ...Object.values(IdentityAccountErrorCodes),
  ...Object.values(ValidationMessageCodes),
].map(errorCodeKey);

describe('admin translations', () => {
  it('finds the keys the admin screens use', () => {
    expect(usedKeys().length).toBeGreaterThan(100);
  });

  it.each([['ar', ar], ['en', en]])('has every admin key and error code in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    const missing = [...usedKeys(), ...ERROR_KEYS].filter((key) => !flat[key]);
    expect(missing).toEqual([]);
  });
});
