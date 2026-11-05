#!/usr/bin/env node
// Phase 4 exit check (decision D5): no reference to an external DGA npm package in the frontend or the npm packages.
// It fails when the dev scope or the short DGA scope followed by "/" appears in any package.json, package-lock.json,
// angular.json, .scss or .ts file under frontend/ or packages/npm/, or in the workspace manifest and lockfile at the
// root (which carry every workspace's dependencies). Dependency and build folders are skipped.
//
// The scrub sweep (CI step 1) applies a broader DGA package rule to every file; this check is the focused, named
// exit criterion and runs in CI step 10 through its test.
//
// Usage: node tools/ci/no-external-dga.mjs [--root <path>]
// Exit codes: 0 clean, 1 references found, 2 usage error.
//
// The scopes are assembled at run time, so this file never contains one.

import { readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const EXIT_CLEAN = 0;
export const EXIT_FINDINGS = 1;
export const EXIT_USAGE = 2;

const AT = '@';
/** The external DGA scopes: the dev scope (any reference) and the short scope as a package path. */
export const EXTERNAL_DGA_SCOPES = Object.freeze([`${AT}dev-dga`, `${AT}dga/`]);
/** The folders searched, relative to the root. */
export const SEARCHED_FOLDERS = Object.freeze(['frontend', 'packages/npm']);
/** Root files searched as well: the workspace manifest and lockfile. */
export const ROOT_FILES = Object.freeze(['package.json', 'package-lock.json']);
/** File names searched wherever they are. */
export const SEARCHED_FILE_NAMES = Object.freeze(['package.json', 'package-lock.json', 'angular.json']);
/** Extensions searched. */
export const SEARCHED_EXTENSIONS = Object.freeze(['.scss', '.ts']);
/** Dependency, build and tool folders: never sources. */
export const SKIPPED_FOLDERS = Object.freeze(['node_modules', 'dist', '.angular', 'coverage', 'bin', 'obj']);

const ROOT_OPTION = '--root';
const DEFAULT_ROOT = '.';
const ROOT_ARGUMENTS = 2;
const LINE_BREAK = '\n';
const FIRST_LINE = 1;
/** process.argv holds node and this script before the arguments. */
const ARGUMENTS_START = 2;

/** True when a file name is one the check reads. */
export function isSearchedFile(name) {
  return SEARCHED_FILE_NAMES.includes(name) || SEARCHED_EXTENSIONS.some((extension) => name.endsWith(extension));
}

/** Returns `{ line, scope }` for every external DGA scope in `text` (1-based lines; one finding per scope per line). */
export function scanText(text) {
  return text.split(LINE_BREAK).flatMap((line, index) => EXTERNAL_DGA_SCOPES
    .filter((scope) => line.toLowerCase().includes(scope))
    .map((scope) => ({ line: index + FIRST_LINE, scope })));
}

function listFolder(root, relativeFolder) {
  const absolute = path.join(root, relativeFolder);
  if (!statSync(absolute, { throwIfNoEntry: false })?.isDirectory()) return [];
  const files = [];
  for (const entry of readdirSync(absolute, { withFileTypes: true })) {
    const relative = `${relativeFolder}/${entry.name}`;
    if (entry.isDirectory()) {
      if (!SKIPPED_FOLDERS.includes(entry.name)) files.push(...listFolder(root, relative));
    } else if (entry.isFile() && isSearchedFile(entry.name)) {
      files.push(relative);
    }
  }
  return files;
}

/** The root-relative files ('/' separators) the check reads, sorted. */
export function listSearchedFiles(root) {
  const rootFiles = ROOT_FILES.filter((file) => statSync(path.join(root, file), { throwIfNoEntry: false })?.isFile());
  return [...rootFiles, ...SEARCHED_FOLDERS.flatMap((folder) => listFolder(root, folder))].sort();
}

/** Scans the searched files under `root`; returns `{ file, line, scope }` findings. */
export function findExternalDgaReferences(root) {
  return listSearchedFiles(root).flatMap((file) => scanText(readFileSync(path.join(root, file), 'utf8')).map((finding) => ({ file, ...finding })));
}

/** Parses argv (after the script path); returns `{ root }` or `{ error }`. */
export function parseArguments(argv) {
  if (argv.length === 0) return { root: DEFAULT_ROOT };
  if (argv.length === ROOT_ARGUMENTS && argv[0] === ROOT_OPTION) return { root: argv[1] };
  return { error: `Usage: node tools/ci/no-external-dga.mjs [${ROOT_OPTION} <path>]` };
}

/** Runs the check; returns the exit code. */
export function main(argv, log = console.log, logError = console.error) {
  const { root, error } = parseArguments(argv);
  if (error) {
    logError(error);
    return EXIT_USAGE;
  }
  const fullRoot = path.resolve(root);
  if (!statSync(fullRoot, { throwIfNoEntry: false })?.isDirectory()) {
    logError(`no external DGA: root '${fullRoot}' is not a folder.`);
    return EXIT_USAGE;
  }
  const findings = findExternalDgaReferences(fullRoot);
  for (const { file, line, scope } of findings) log(`${file}:${line}: external DGA package scope '${scope}' (D5: use @codelab/ng-dga)`);
  if (findings.length === 0) {
    log(`no external DGA: PASSED (${listSearchedFiles(fullRoot).length} file(s))`);
    return EXIT_CLEAN;
  }
  log(`no external DGA: FAILED with ${findings.length} reference(s)`);
  return EXIT_FINDINGS;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(ARGUMENTS_START));
}
