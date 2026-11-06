import { readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import ar from '../../../../assets/i18n/ar.json';
import en from '../../../../assets/i18n/en.json';
import { flattenTranslations } from '../../../core/i18n/translation.service';
import { SETTING_GROUP_KEY_PREFIX, SettingsTranslationKeys } from './settings-translation-keys';

const FOLDER = dirname(fileURLToPath(import.meta.url));
const SOURCE_FILE = /\.(ts|html)$/;
const NOT_SOURCE = /\.(spec|testing)\.ts$/;
/** A key literal of this screen, in single or double quotes. */
const KEY_LITERAL = /['"](admin\.settings(?:\.[a-z0-9-]+)+)['"]/g;
/** The admin home's tile for this screen (`admin-page/admin-tiles.ts`). */
const TILE_KEYS = ['admin.home.settings.title', 'admin.home.settings.text'];

function leaves(node: unknown): string[] {
  return typeof node === 'string' ? [node] : Object.values(node as object).flatMap(leaves);
}

const DECLARED = leaves(SettingsTranslationKeys);

/** The server's seed of the settings (repository root, seven folders above this one), the source of the groups. */
const SETTINGS_SEED = join(FOLDER, '..', '..', '..', '..', '..', '..', '..', 'backend', 'src', 'Modules', 'Settings', 'Infrastructure', 'Persistence', 'Migrations', 'SettingsSeed.cs');
/** A seeded group constant: `public const string SessionGroup = "session";`. */
const SEEDED_GROUP = /const string \w+Group = "([a-z0-9-]+)";/g;

function seededGroups(): string[] {
  return [...readFileSync(SETTINGS_SEED, 'utf8').matchAll(SEEDED_GROUP)].map((match) => match[1]);
}

describe('settings translations', () => {
  it('declares every key the screen\'s sources name', () => {
    const named = readdirSync(FOLDER)
      .filter((name) => SOURCE_FILE.test(name) && !NOT_SOURCE.test(name))
      .flatMap((name) => [...readFileSync(join(FOLDER, name), 'utf8').matchAll(KEY_LITERAL)].map((match) => match[1]));
    expect(named.filter((key) => !DECLARED.includes(key))).toEqual([]);
  });

  it.each([['ar', ar], ['en', en]])('has every settings text in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect([...DECLARED, ...TILE_KEYS].filter((key) => !flat[key])).toEqual([]);
  });

  it('declares a name key for every group the server seeds, and only those', () => {
    const groups = seededGroups();
    expect(groups.length).toBeGreaterThan(0);
    expect(Object.values(SettingsTranslationKeys.Groups).sort()).toEqual(groups.map((group) => `${SETTING_GROUP_KEY_PREFIX}${group}`).sort());
  });

  it.each([['ar', ar], ['en', en]])('names every seeded group in the %s bundle', (_language, bundle) => {
    const flat = flattenTranslations(bundle);
    expect(seededGroups().filter((group) => !flat[`${SETTING_GROUP_KEY_PREFIX}${group}`])).toEqual([]);
  });
});
