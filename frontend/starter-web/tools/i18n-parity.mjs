#!/usr/bin/env node
// i18n key parity (base rule base.fe-no-raw-text): every translation file under src/assets/i18n has exactly the same
// nested keys as the others, the required languages (ar, en) exist, and every leaf is a non-empty string.
//
//   node tools/i18n-parity.mjs [--dir <folder>]     exit 0 ok, 1 a problem was found, 2 usage
//
// Part of `npm run lint`.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/** Default folder, relative to the app folder (frontend/starter-web). */
export const DEFAULT_I18N_DIR = 'src/assets/i18n';

/** Languages that must exist; Arabic first (the default language). */
export const REQUIRED_LANGUAGES = Object.freeze(['ar', 'en']);

export const ExitCodes = Object.freeze({ ok: 0, failure: 1, usage: 2 });

const DIR_FLAG = '--dir';
const JSON_EXTENSION = '.json';
const KEY_SEPARATOR = '.';
const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/**
 * Flattens a translation object into `Map<dottedKey, leaf>`. Problems (arrays, non-string or empty leaves, empty
 * objects) are pushed onto `problems` with the dotted key.
 */
export function flattenTranslations(value, problems = [], prefix = '') {
  const leaves = new Map();
  const visit = (node, key) => {
    if (typeof node === 'string') {
      if (node.trim() === '') problems.push(`${key}: empty text`);
      leaves.set(key, node);
    } else if (node !== null && typeof node === 'object' && !Array.isArray(node)) {
      const entries = Object.entries(node);
      if (entries.length === 0 && key !== '') problems.push(`${key}: empty group`);
      for (const [child, childValue] of entries) visit(childValue, key === '' ? child : key + KEY_SEPARATOR + child);
    } else {
      problems.push(`${key || '(root)'}: expected text or a group, found ${Array.isArray(node) ? 'an array' : typeof node}`);
    }
  };
  visit(value, prefix);
  return leaves;
}

/**
 * Compares the key sets of every language. `languages` is `[{ language, keys: Set<string> }]`.
 * Returns one problem line per key missing from a language, sorted.
 */
export function compareKeySets(languages) {
  const union = new Set(languages.flatMap(({ keys }) => [...keys]));
  const problems = [];
  for (const { language, keys } of languages) {
    for (const key of [...union].sort()) {
      if (!keys.has(key)) problems.push(`${language}: missing key ${key}`);
    }
  }
  return problems;
}

/** Reads every `*.json` in `dir` and returns `{ problems: string[], languages: string[] }`. */
export function checkI18nParity(dir) {
  if (!fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) {
    return { problems: [`translation folder not found: ${dir}`], languages: [] };
  }
  const files = fs.readdirSync(dir).filter((name) => name.endsWith(JSON_EXTENSION)).sort();
  const problems = [];
  const languages = [];
  for (const file of files) {
    const language = file.slice(0, -JSON_EXTENSION.length);
    let document;
    try {
      document = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
    } catch (error) {
      problems.push(`${file}: not valid JSON (${error.message})`);
      continue;
    }
    const fileProblems = [];
    const keys = new Set(flattenTranslations(document, fileProblems).keys());
    problems.push(...fileProblems.map((problem) => `${language}: ${problem}`));
    languages.push({ language, keys });
  }
  const present = new Set(languages.map(({ language }) => language));
  for (const required of REQUIRED_LANGUAGES) {
    if (!present.has(required) && !files.includes(required + JSON_EXTENSION)) problems.push(`${required}: ${required}${JSON_EXTENSION} is missing`);
  }
  problems.push(...compareKeySets(languages));
  return { problems, languages: languages.map(({ language }) => language) };
}

/** Parses argv; returns `{ dir }` or `{ error }`. */
export function parseArguments(argv, appDir = APP_DIR) {
  if (argv.length === 0) return { dir: path.resolve(appDir, DEFAULT_I18N_DIR) };
  if (argv.length === 2 && argv[0] === DIR_FLAG) return { dir: path.resolve(appDir, argv[1]) };
  return { error: `Usage: node tools/i18n-parity.mjs [${DIR_FLAG} <folder>]` };
}

/** Runs the check; returns the exit code. */
export function run(argv, { appDir = APP_DIR, log = console.log, error = console.error } = {}) {
  const options = parseArguments(argv, appDir);
  if (options.error) {
    error(options.error);
    return ExitCodes.usage;
  }
  const { problems, languages } = checkI18nParity(options.dir);
  if (problems.length > 0) {
    error(`i18n parity failed (${problems.length}):\n${problems.map((problem) => `  ${problem}`).join('\n')}`);
    return ExitCodes.failure;
  }
  log(`i18n parity ok: ${languages.join(', ')}`);
  return ExitCodes.ok;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = run(process.argv.slice(2));
}
