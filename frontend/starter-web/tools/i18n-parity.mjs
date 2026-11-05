#!/usr/bin/env node
// The i18n check (base rule base.fe-no-raw-text) for the starter app. The check is @codelab/ng-tools'
// (`codelab-ng-tools i18n-check`): every file in src/assets/i18n has the same nested keys, ar and en exist, every leaf
// is non-empty text, every Arabic text has Arabic letters, and every key src/app uses literally (piped through `t`,
// a `<x>Key` input, translate(...), or a `*translation-keys.ts` catalogue) exists in both languages. This wrapper
// fixes the app's folders and the Arabic keys that are deliberately Latin.
//
//   node tools/i18n-parity.mjs [--dir <translations>] [--src <folder>]     exit 0 ok, 1 a problem, 2 usage
//
// Part of `npm run lint`.

import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ExitCodes, runI18nCheck } from '@codelab/ng-tools';

export { ExitCodes };

/** Default folders, relative to the app folder (frontend/starter-web). */
export const DEFAULT_I18N_DIR = 'src/assets/i18n';
export const DEFAULT_SOURCE_DIR = 'src/app';
/** Arabic texts that are Latin on purpose: the language switcher names English in English. */
export const LATIN_ALLOWED_KEYS = Object.freeze(['layout.language-switcher.en']);

const Flags = Object.freeze({ dir: '--dir', src: '--src' });
const USAGE = `Usage: node tools/i18n-parity.mjs [${Flags.dir} <translations>] [${Flags.src} <folder>]`;
const PAIR = 2;
/** process.argv holds node and this script before the arguments. */
const ARGUMENTS_START = 2;
const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** Parses argv; returns `{ dir, src }` (absolute) or `{ error }`. */
export function parseArguments(argv, appDir = APP_DIR) {
  const options = { dir: DEFAULT_I18N_DIR, src: DEFAULT_SOURCE_DIR };
  if (argv.length % PAIR !== 0) return { error: USAGE };
  for (let index = 0; index < argv.length; index += PAIR) {
    const flag = Object.entries(Flags).find(([, name]) => name === argv[index]);
    if (flag === undefined) return { error: USAGE };
    options[flag[0]] = argv[index + 1];
  }
  return { dir: path.resolve(appDir, options.dir), src: path.resolve(appDir, options.src) };
}

/** The `codelab-ng-tools i18n-check` arguments for the parsed options. */
export function checkArguments({ dir, src }) {
  return ['--dir', dir, '--src', src, ...LATIN_ALLOWED_KEYS.flatMap((key) => ['--allow-latin', key])];
}

/** Runs the check; returns the exit code. */
export function run(argv, { appDir = APP_DIR, log = console.log, error = console.error } = {}) {
  const options = parseArguments(argv, appDir);
  if (options.error) {
    error(options.error);
    return ExitCodes.usage;
  }
  return runI18nCheck(checkArguments(options), { log, error });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = run(process.argv.slice(ARGUMENTS_START));
}
