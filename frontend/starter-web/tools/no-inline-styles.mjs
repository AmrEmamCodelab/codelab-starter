#!/usr/bin/env node
// No inline styles in the .html templates (base.fe-design-tokens, base.fe-logical-css): `style="..."`, `[style]`,
// `[style.x]`, `[attr.style]` and `[ngStyle]` are refused, because Stylelint checks only .scss and .css files. The .ts
// side (component `styles`, inline `template`, `host`, NgStyle) is ESLint's (tools/lint/inline-styles.mjs).
//
//   node tools/no-inline-styles.mjs [--dir <folder>]     exit 0 ok, 1 a problem was found, 2 usage
//
// Part of `npm run lint`.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { INLINE_STYLE_MESSAGE, templateStylePattern } from './lint/inline-styles.mjs';

/** Default folder, relative to the app folder (frontend/starter-web). */
export const DEFAULT_SOURCE_DIR = 'src';

export const ExitCodes = Object.freeze({ ok: 0, failure: 1, usage: 2 });

const DIR_FLAG = '--dir';
const HTML_EXTENSION = '.html';
const LINE_BREAK = /\r?\n/;
const FIRST_LINE = 1;
const FIRST_COLUMN = 1;
const SKIPPED_FOLDERS = new Set(['node_modules', 'dist', 'coverage', '.angular']);
const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/** Returns `[{ line, column, text }]` (1-based) for every inline style in one template's text. */
export function findInlineStyles(text) {
  const pattern = templateStylePattern('gi');
  const findings = [];
  text.split(LINE_BREAK).forEach((lineText, index) => {
    for (const match of lineText.matchAll(pattern)) {
      findings.push({ line: index + FIRST_LINE, column: match.index + FIRST_COLUMN, text: match[0] });
    }
  });
  return findings;
}

/** Every `*.html` file under `dir`, sorted, skipping build output and dependencies. */
export function listTemplates(dir) {
  const files = [];
  const visit = (folder) => {
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
      const entryPath = path.join(folder, entry.name);
      if (entry.isDirectory()) {
        if (!SKIPPED_FOLDERS.has(entry.name)) visit(entryPath);
      } else if (entry.isFile() && entry.name.endsWith(HTML_EXTENSION)) {
        files.push(entryPath);
      }
    }
  };
  visit(dir);
  return files.sort();
}

/** Checks every template under `dir`; returns `{ problems: string[], fileCount: number }`. */
export function checkTemplates(dir) {
  if (!fs.existsSync(dir) || !fs.statSync(dir).isDirectory()) {
    return { problems: [`source folder not found: ${dir}`], fileCount: 0 };
  }
  const files = listTemplates(dir);
  const problems = files.flatMap((file) =>
    findInlineStyles(fs.readFileSync(file, 'utf8')).map(({ line, column, text }) =>
      `${path.relative(dir, file).split(path.sep).join('/')}:${line}:${column} ${text.trim()}`));
  return { problems, fileCount: files.length };
}

/** Parses argv; returns `{ dir }` or `{ error }`. */
export function parseArguments(argv, appDir = APP_DIR) {
  if (argv.length === 0) return { dir: path.resolve(appDir, DEFAULT_SOURCE_DIR) };
  if (argv.length === 2 && argv[0] === DIR_FLAG) return { dir: path.resolve(appDir, argv[1]) };
  return { error: `Usage: node tools/no-inline-styles.mjs [${DIR_FLAG} <folder>]` };
}

/** Runs the check; returns the exit code. */
export function run(argv, { appDir = APP_DIR, log = console.log, error = console.error } = {}) {
  const options = parseArguments(argv, appDir);
  if (options.error) {
    error(options.error);
    return ExitCodes.usage;
  }
  const { problems, fileCount } = checkTemplates(options.dir);
  if (problems.length > 0) {
    error(`${INLINE_STYLE_MESSAGE}\n${problems.map((problem) => `  ${problem}`).join('\n')}`);
    return ExitCodes.failure;
  }
  log(`no inline styles: ${fileCount} templates ok`);
  return ExitCodes.ok;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = run(process.argv.slice(2));
}
