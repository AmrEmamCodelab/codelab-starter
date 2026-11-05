#!/usr/bin/env node
// The RTL lint (base rule base.fe-tokens-logical-css, templates and TypeScript) for the starter app. The check is
// @codelab/ng-tools' (`codelab-ng-tools rtl-lint`): no physical-side style bindings, inline styles, alignments or
// floats, no hard-coded dir="ltr"/"rtl" outside src/index.html (or an element marked data-fixed-direction), no
// physically named icons and no mirroring switched off on a directional icon. The icon rules read @codelab/ng-dga's
// shipped icon map. This wrapper fixes the folder, the map and the host page.
//
//   node tools/rtl-lint.mjs [--dir <folder>]     exit 0 ok, 1 a finding, 2 usage
//
// Part of `npm run lint`.

import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ExitCodes, runRtlLint } from '@codelab/ng-tools';

export { ExitCodes };

/** Default folder, relative to the app folder (frontend/starter-web): every template and source, index.html included. */
export const DEFAULT_SOURCE_DIR = 'src';
/** The page that sets the document direction before the app starts (relative to the scanned folder). */
export const DIRECTION_FILES = Object.freeze(['index.html']);
/** The icon code point map @codelab/ng-dga ships, relative to its package folder. */
export const ICON_MAP = 'assets/icons/codelab-icons.json';
const NG_DGA_MANIFEST = '@codelab/ng-dga/package.json';

const DIR_FLAG = '--dir';
const DIR_ARGUMENTS = 2;
/** process.argv holds node and this script before the arguments. */
const ARGUMENTS_START = 2;
const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** The folder of the installed @codelab/ng-dga package. */
export function ngDgaFolder() {
  return path.dirname(createRequire(import.meta.url).resolve(NG_DGA_MANIFEST));
}

/** Parses argv; returns `{ dir }` (absolute) or `{ error }`. */
export function parseArguments(argv, appDir = APP_DIR) {
  if (argv.length === 0) return { dir: path.resolve(appDir, DEFAULT_SOURCE_DIR) };
  if (argv.length === DIR_ARGUMENTS && argv[0] === DIR_FLAG) return { dir: path.resolve(appDir, argv[1]) };
  return { error: `Usage: node tools/rtl-lint.mjs [${DIR_FLAG} <folder>]` };
}

/** The `codelab-ng-tools rtl-lint` arguments for a folder. */
export function lintArguments(dir, iconMap = path.join(ngDgaFolder(), ICON_MAP)) {
  return [dir, '--icons', iconMap, ...DIRECTION_FILES.flatMap((glob) => ['--direction-file', glob])];
}

/** Runs the lint; returns the exit code. */
export function run(argv, { appDir = APP_DIR, log = console.log, error = console.error } = {}) {
  const options = parseArguments(argv, appDir);
  if (options.error) {
    error(options.error);
    return ExitCodes.usage;
  }
  return runRtlLint(lintArguments(options.dir), { log, error });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = run(process.argv.slice(ARGUMENTS_START));
}
