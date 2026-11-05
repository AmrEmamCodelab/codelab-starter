#!/usr/bin/env node
// Icon subsetting for the starter app (fix "icons blocking first paint"): the icons src/app and @codelab/ng-dga's own
// components use, against the icon map ng-dga ships. The check is @codelab/ng-tools' (`codelab-ng-tools
// icons-subset`). In `npm run lint` it fails on a literal icon name the map does not know (it would render nothing)
// and reports the used set; with --out it writes the subset map and, when ng-dga's generator and SVG sources are
// present (the starter repository), the subset font.
//
//   node tools/icons-subset.mjs [--out <folder> [--check]]     exit 0 ok, 1 unknown icon or stale output, 2 usage
//
// `npm run icons:subset` (also the prebuild and prestart step) writes the subset to SUBSET_OUT_DIR, which src/styles.scss
// serves in place of ng-dga's full font; `npm run lint` (CI step 17) runs it with --check and fails on a stale subset.

import fs from 'node:fs';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ExitCodes, runIconsSubset } from '@codelab/ng-tools';

export { ExitCodes };

/** Where the subset map and font are written and committed, relative to the app folder; src/styles.scss loads the font. */
export const SUBSET_OUT_DIR = 'src/generated/icons';
/**
 * The subset's own file stem, so the bundler's flattened media/ folder never holds two different codelab-icons.woff2
 * files (ng-dga's full font and the subset) in a non-hashed (development) build.
 */
export const SUBSET_STEM = 'codelab-icons-subset';
/** Default folder, relative to the app folder (frontend/starter-web). */
export const DEFAULT_SOURCE_DIR = 'src/app';
/** Paths inside the @codelab/ng-dga package folder. */
export const NgDgaPaths = Object.freeze({
  iconMap: 'assets/icons/codelab-icons.json',
  componentSources: 'src/lib',
  generator: 'scripts/lib/icon-font.mjs',
  svgSources: 'icons/svg',
});
const NG_DGA_MANIFEST = '@codelab/ng-dga/package.json';

const Flags = Object.freeze({ out: '--out', check: '--check' });
const USAGE = `Usage: node tools/icons-subset.mjs [${Flags.out} <folder> [${Flags.check}]]`;
/** process.argv holds node and this script before the arguments. */
const ARGUMENTS_START = 2;
const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** The folder of the installed @codelab/ng-dga package. */
export function ngDgaFolder() {
  return path.dirname(createRequire(import.meta.url).resolve(NG_DGA_MANIFEST));
}

/** Parses argv; returns `{ out?, check }` (out absolute) or `{ error }`. */
export function parseArguments(argv, appDir = APP_DIR) {
  const options = { check: false };
  for (let index = 0; index < argv.length; index++) {
    if (argv[index] === Flags.check) {
      options.check = true;
    } else if (argv[index] === Flags.out && argv[index + 1] !== undefined) {
      options.out = path.resolve(appDir, argv[index + 1]);
      index++;
    } else {
      return { error: USAGE };
    }
  }
  if (options.check && options.out === undefined) return { error: USAGE };
  return options;
}

/**
 * The `codelab-ng-tools icons-subset` arguments. ng-dga's component sources are scanned when the package folder has
 * them; the font is built only with --out and when its generator and SVG sources exist.
 */
export function subsetArguments({ out, check }, { appDir = APP_DIR, ngDga = ngDgaFolder() } = {}) {
  const inPackage = (relative) => path.join(ngDga, relative);
  const librarySources = fs.existsSync(inPackage(NgDgaPaths.componentSources)) ? ['--dir', inPackage(NgDgaPaths.componentSources)] : [];
  const args = ['--dir', path.resolve(appDir, DEFAULT_SOURCE_DIR), ...librarySources, '--map', inPackage(NgDgaPaths.iconMap)];
  if (out === undefined) return args;
  const canBuildFont = fs.existsSync(inPackage(NgDgaPaths.generator)) && fs.existsSync(inPackage(NgDgaPaths.svgSources));
  const font = canBuildFont ? ['--generator', inPackage(NgDgaPaths.generator), '--svg-dir', inPackage(NgDgaPaths.svgSources)] : [];
  return [...args, '--out', out, '--stem', SUBSET_STEM, ...font, ...(check ? ['--check'] : [])];
}

/** Runs the subset; resolves to the exit code. */
export async function run(argv, { appDir = APP_DIR, log = console.log, error = console.error } = {}) {
  const options = parseArguments(argv, appDir);
  if (options.error) {
    error(options.error);
    return ExitCodes.usage;
  }
  return runIconsSubset(subsetArguments(options, { appDir }), { log, error });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = await run(process.argv.slice(ARGUMENTS_START));
}
