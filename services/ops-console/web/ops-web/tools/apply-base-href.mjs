#!/usr/bin/env node
// Writes config.json's basePath into index.html's <base href> in a built (or deployed) Ops Console web folder, so the
// console can live as a child app at any path of an existing site (`/ops/`) from ONE build. Run it after editing
// basePath in the deployed config.json; the app refuses to start while the two differ (BasePathMismatchError).
//
//   node tools/apply-base-href.mjs <folder holding index.html and config.json>
//
// Exit codes: 0 written (or already equal), 1 invalid input (missing file, bad basePath, no <base> tag), 2 usage.

import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const EXIT_OK = 0;
export const EXIT_INVALID = 1;
export const EXIT_USAGE = 2;

export const INDEX_FILE = 'index.html';
export const CONFIG_FILE = 'config.json';
export const BASE_PATH_KEY = 'basePath';

/** The one <base href="..."> tag Angular emits; the value is replaced, the rest of the tag is kept. */
const BASE_TAG = /(<base\b[^>]*\bhref=")([^"]*)(")/i;
/** A site-relative path ending in "/", with no "//" prefix, backslash, "..", quote or angle bracket. */
const VALID_BASE_PATH = /^\/(?!\/)(?:[^\\"<>]*\/)?$/;
const PARENT_SEGMENT = '/../';
const ARGUMENTS_START = 2;

export class BaseHrefError extends Error {
  constructor(message) {
    super(message);
    this.name = 'BaseHrefError';
  }
}

/** True for a basePath the console accepts (same rule as parseOpsConfig). */
export function isValidBasePath(value) {
  return typeof value === 'string' && VALID_BASE_PATH.test(value) && !`${value}`.includes(PARENT_SEGMENT);
}

/** `html` with its <base href> set to `basePath`. @throws BaseHrefError for a bad path or a page without the tag. */
export function withBaseHref(html, basePath) {
  if (!isValidBasePath(basePath)) {
    throw new BaseHrefError(`${CONFIG_FILE}: "${BASE_PATH_KEY}" must be a site-relative path ending in "/".`);
  }
  if (!BASE_TAG.test(html)) {
    throw new BaseHrefError(`${INDEX_FILE} has no <base href> tag.`);
  }
  return html.replace(BASE_TAG, (_match, start, _old, end) => `${start}${basePath}${end}`);
}

/** Reads both files in `folder`, rewrites index.html when the base href differs. Returns the applied base path. */
export function applyBaseHref(folder) {
  const indexPath = path.join(folder, INDEX_FILE);
  let config;
  let html;
  try {
    config = JSON.parse(readFileSync(path.join(folder, CONFIG_FILE), 'utf8'));
    html = readFileSync(indexPath, 'utf8');
  } catch (error) {
    throw new BaseHrefError(`Cannot read ${INDEX_FILE} and ${CONFIG_FILE} in ${folder}: ${error.message}`);
  }
  const basePath = config?.[BASE_PATH_KEY];
  const updated = withBaseHref(html, basePath);
  if (updated !== html) {
    writeFileSync(indexPath, updated, 'utf8');
  }
  return basePath;
}

function main(argv) {
  if (argv.length !== 1) {
    console.error('Usage: node tools/apply-base-href.mjs <web folder>');
    return EXIT_USAGE;
  }
  try {
    console.log(`<base href> is now ${applyBaseHref(path.resolve(argv[0]))}`);
    return EXIT_OK;
  } catch (error) {
    if (error instanceof BaseHrefError) {
      console.error(error.message);
      return EXIT_INVALID;
    }
    throw error;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = main(process.argv.slice(ARGUMENTS_START));
}
