#!/usr/bin/env node
// Phase 7 exit check: a production build contains no DevAutofill code. The web app's production configuration
// swaps the real DevAutofill implementation and the app's data-set registry for stubs (angular.json
// fileReplacements); this check proves the swap on the built output. It scans the bundle files of a dist folder
// (.js, .mjs, .html, .css) for:
//   - DEV_AUTOFILL_MARKER, the string only the real implementation renders
//     (frontend/starter-web/src/app/shared/dev-autofill/dev-autofill.impl.ts), and
//   - DEV_AUTOFILL_SET_MARKER, the `devAutofill` i18n key segment every data set's label key carries, so a data set
//     that reached the bundle is found even when the implementation did not;
// plus any extra --marker given (a known value of a feature's data set). Translation bundles (.json) are not
// scanned: the button's ar/en text is data, not code.
//
// Usage: node tools/ci/no-dev-autofill.mjs --dist <folder> [--marker <text>]...
// Exit codes: 0 clean, 1 markers found, 2 usage error (including a dist folder with no bundle file).

import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const EXIT_CLEAN = 0;
export const EXIT_FINDINGS = 1;
export const EXIT_USAGE = 2;

/** Equal to DEV_AUTOFILL_MARKER in dev-autofill.impl.ts (the test checks it). */
export const DEV_AUTOFILL_MARKER = 'codelab-dev-autofill-impl';
/** The `devAutofill` label-key segment with its dots (DEV_AUTOFILL_LABEL_SEGMENT in dev-autofill-keys.ts). */
export const DEV_AUTOFILL_SET_MARKER = '.devAutofill.';
/** The markers searched without any --marker. */
export const DEFAULT_MARKERS = Object.freeze([DEV_AUTOFILL_MARKER, DEV_AUTOFILL_SET_MARKER]);
/** The bundle files a build emits. */
export const SCANNED_EXTENSIONS = Object.freeze(['.js', '.mjs', '.html', '.css']);

const DIST_OPTION = '--dist';
const MARKER_OPTION = '--marker';
const OPTION_WITH_VALUE = 2;
/** process.argv holds node and this script before the arguments. */
const ARGUMENTS_START = 2;
const USAGE = `Usage: node tools/ci/no-dev-autofill.mjs ${DIST_OPTION} <folder> [${MARKER_OPTION} <text>]...`;

/** True when a file name is a bundle file the check reads. */
export function isScannedFile(name) {
  return SCANNED_EXTENSIONS.some((extension) => name.toLowerCase().endsWith(extension));
}

/** The markers that occur in `text`, in the order given. */
export function scanText(text, markers = DEFAULT_MARKERS) {
  return markers.filter((marker) => text.includes(marker));
}

/** The dist-relative bundle files ('/' separators) under `dist`, sorted. */
export function listBundleFiles(dist, relativeFolder = '') {
  const files = [];
  for (const entry of readdirSync(path.join(dist, relativeFolder), { withFileTypes: true })) {
    const relative = relativeFolder === '' ? entry.name : `${relativeFolder}/${entry.name}`;
    if (entry.isDirectory()) files.push(...listBundleFiles(dist, relative));
    else if (entry.isFile() && isScannedFile(entry.name)) files.push(relative);
  }
  return files.sort();
}

/** `{ file, marker }` for every marker in every bundle file under `dist`. */
export function findDevAutofillMarkers(dist, markers = DEFAULT_MARKERS) {
  return listBundleFiles(dist).flatMap((file) =>
    scanText(readFileSync(path.join(dist, file), 'utf8'), markers).map((marker) => ({ file, marker })));
}

/** Parses argv (after the script path); returns `{ dist, markers }` or `{ error }`. */
export function parseArguments(argv) {
  let dist = null;
  const extra = [];
  for (let index = 0; index < argv.length; index += OPTION_WITH_VALUE) {
    const [option, value] = [argv[index], argv[index + 1]];
    if (value === undefined || value === '') return { error: USAGE };
    if (option === DIST_OPTION && dist === null) dist = value;
    else if (option === MARKER_OPTION) extra.push(value);
    else return { error: USAGE };
  }
  if (dist === null) return { error: USAGE };
  return { dist, markers: [...DEFAULT_MARKERS, ...extra.filter((marker) => !DEFAULT_MARKERS.includes(marker))] };
}

/** Runs the check; returns the exit code. */
export function main(argv, log = console.log, logError = console.error) {
  const { dist, markers, error } = parseArguments(argv);
  if (error) {
    logError(error);
    return EXIT_USAGE;
  }
  const fullDist = path.resolve(dist);
  if (!statSync(fullDist, { throwIfNoEntry: false })?.isDirectory()) {
    logError(`no dev autofill: dist '${fullDist}' is not a folder.`);
    return EXIT_USAGE;
  }
  const fileCount = listBundleFiles(fullDist).length;
  if (fileCount === 0) {
    logError(`no dev autofill: dist '${fullDist}' holds no bundle file (${SCANNED_EXTENSIONS.join(', ')}); build first.`);
    return EXIT_USAGE;
  }
  const findings = findDevAutofillMarkers(fullDist, markers);
  for (const { file, marker } of findings) log(`${file}: DevAutofill marker '${marker}' in a production bundle`);
  if (findings.length === 0) {
    log(`no dev autofill: PASSED (${fileCount} file(s), ${markers.length} marker(s))`);
    return EXIT_CLEAN;
  }
  log(`no dev autofill: FAILED with ${findings.length} finding(s)`);
  return EXIT_FINDINGS;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(ARGUMENTS_START));
}
