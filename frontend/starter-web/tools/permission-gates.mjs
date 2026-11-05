#!/usr/bin/env node
// The template permission gate (base.fe-permission-gated) over the feature templates: every action element in
// features/**/*.html and in the inline templates of features/**/*.ts (a <dga-button>; a <button> with any event binding,
// a submit button or a typeless one inside a submitting form; a role=button element with an event binding; any element
// that binds (click) or (confirm), such as a dialog host) carries
// *hasPermission (on itself or an ancestor; [hasPermission] on an <ng-template> wrapper) or data-public-action. The
// check is @codelab/ng-tools' (`permission-gates`); this wrapper fixes the folder and points the fix at the recipe.
//
//   node tools/permission-gates.mjs [--dir <folder>]     exit 0 ok, 1 an ungated button, 2 usage
//
// Part of `npm run lint`.

import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { PERMISSION_GATE_FIX, checkPermissionGates, formatGateFindings } from '@codelab/ng-tools/permission-gates';

/** Default folder, relative to the app folder (frontend/starter-web). */
export const DEFAULT_FEATURES_DIR = 'src/app/features';
export const RECIPE = 'docs/recipes/add-permission.md';
export const STARTER_GATE_FIX = `${PERMISSION_GATE_FIX} Recipe: ${RECIPE}.`;

export const ExitCodes = Object.freeze({ ok: 0, failure: 1, usage: 2 });

const DIR_FLAG = '--dir';
const DIR_ARGUMENTS = 2;
/** process.argv holds node and this script before the arguments. */
const ARGUMENTS_START = 2;
const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** Parses argv; returns `{ dir }` or `{ error }`. */
export function parseArguments(argv, appDir = APP_DIR) {
  if (argv.length === 0) return { dir: path.resolve(appDir, DEFAULT_FEATURES_DIR) };
  if (argv.length === DIR_ARGUMENTS && argv[0] === DIR_FLAG) return { dir: path.resolve(appDir, argv[1]) };
  return { error: `Usage: node tools/permission-gates.mjs [${DIR_FLAG} <folder>]` };
}

/** Runs the check; returns the exit code. */
export function run(argv, { appDir = APP_DIR, log = console.log, error = console.error } = {}) {
  const options = parseArguments(argv, appDir);
  if (options.error) {
    error(options.error);
    return ExitCodes.usage;
  }
  const result = checkPermissionGates(options.dir);
  if (result.error) {
    error(result.error);
    return ExitCodes.usage;
  }
  if (result.findings.length > 0) {
    error(formatGateFindings(result.findings, STARTER_GATE_FIX));
    return ExitCodes.failure;
  }
  log(`permission gates: ${result.fileCount} feature files, every action button gated`);
  return ExitCodes.ok;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = run(process.argv.slice(ARGUMENTS_START));
}
