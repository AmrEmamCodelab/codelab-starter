// Command-line parsing of e2e/visual.mjs (pure: argv in, options or an error out).
import os from 'node:os';
import path from 'node:path';
import {
  BASELINE_MAX_DIFF_RATIO,
  DEFAULT_LANGUAGE_STORAGE_KEY,
  DEFAULT_LANGUAGES,
  DEFAULT_MASK_SELECTORS,
  DEFAULT_OUTPUT_FOLDER,
  DEFAULT_SETTLE_MS,
  DEFAULT_WIDTHS,
  MAX_WIDTH,
  MIN_WIDTH,
  Modes,
  PIXEL_THRESHOLD,
  SUPPORTED_LANGUAGES,
  TARGET_MAX_DIFF_RATIO,
} from './constants.mjs';

export const USAGE = `Usage: node e2e/visual.mjs --base-url <url> --routes <file|json> [options]

Baseline mode (default): compares each screenshot with <baseline-dir>/<route>.<lang>.<width>.png.
Target mode (--target): compares each screenshot with a design target image.

  --base-url <url>           the running app, for example http://localhost:4280
  --routes <file|json>       routes file (or inline JSON): [{ "name", "path", "waitFor"?, "mask"? }] or
                             { "routes": [...], "mask"?: [...], "languageStorageKey"?: "..." }
  --langs <list>             languages, default ar,en
  --widths <list>            viewport widths in px, default 1440,390
  --baseline-dir <dir>       stored baselines (required in baseline mode)
  --update                   write the screenshots as the new baselines instead of comparing
  --target <png>             target image; may contain {route}, {lang} and {width}
  --threshold <ratio>        allowed share of differing pixels (default 0.001 baseline, 0.05 target)
  --pixel-threshold <0..1>   pixelmatch colour tolerance, default 0.1
  --mask <selector>          extra selector to mask (repeatable); [data-visual-mask] is always masked
  --out-dir <dir>            screenshots, diffs and the JSON + Markdown report (default: %TEMP%/codelab-visual)
  --settle-ms <ms>           quiet time before each screenshot, default 500
  --language-storage-key <k> browser storage key holding the app language, default starter.language
  --iteration <n>            iteration number recorded in the report (design-agent loop)
  --label <text>             free label recorded in the report
  --no-axe                   skip the accessibility scan
  --help                     this text`;

const LIST_SEPARATOR = ',';
const MIN_RATIO = 0;
const MAX_RATIO = 1;
const FIRST_ITERATION = 1;
/** Characters of an ISO timestamp that are not safe in a Windows folder name. */
const TIMESTAMP_UNSAFE = /[:.]/g;

/** Flags that take no value. */
const SWITCHES = new Set(['--update', '--no-axe', '--help']);
/** Flags that may repeat. */
const REPEATABLE = new Set(['--mask']);
const KNOWN_VALUE_FLAGS = new Set([
  '--base-url',
  '--routes',
  '--langs',
  '--widths',
  '--baseline-dir',
  '--target',
  '--threshold',
  '--pixel-threshold',
  '--mask',
  '--out-dir',
  '--settle-ms',
  '--language-storage-key',
  '--iteration',
  '--label',
]);

/** Splits argv into { flags: Map<flag, string[]>, switches: Set<flag> } or { error }. */
export function tokenize(argv) {
  const flags = new Map();
  const switches = new Set();
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (SWITCHES.has(argument)) {
      switches.add(argument);
      continue;
    }
    if (!KNOWN_VALUE_FLAGS.has(argument)) return { error: `Unknown option: ${argument}` };
    const value = argv[index + 1];
    if (value === undefined || KNOWN_VALUE_FLAGS.has(value) || SWITCHES.has(value)) {
      return { error: `${argument} needs a value.` };
    }
    if (flags.has(argument) && !REPEATABLE.has(argument)) return { error: `${argument} is given twice.` };
    flags.set(argument, [...(flags.get(argument) ?? []), value]);
    index += 1;
  }
  return { flags, switches };
}

function list(value) {
  return value
    .split(LIST_SEPARATOR)
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

function ratio(value, flag) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < MIN_RATIO || parsed > MAX_RATIO) return { error: `${flag} must be a number from 0 to 1.` };
  return { value: parsed };
}

function wholeNumber(value, flag, minimum) {
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < minimum) return { error: `${flag} must be a whole number of at least ${minimum}.` };
  return { value: parsed };
}

/** The storage key to seed: the command line wins, then the routes file, then the app's own key. */
export function resolveLanguageStorageKey(fromArgs, fromRoutes) {
  return fromArgs ?? fromRoutes ?? DEFAULT_LANGUAGE_STORAGE_KEY;
}

/**
 * Parses the command line: { options } or { error } or { help: true }.
 * options: { mode, baseUrl, routes (raw string), languages, widths, baselineDir, update, target, maxDiffRatio,
 * pixelThreshold, maskSelectors, outDir, settleMs, languageStorageKey, iteration, label, axe }.
 */
export function parseArgs(argv, { tempDir = os.tmpdir(), now = new Date() } = {}) {
  const tokens = tokenize(argv);
  if (tokens.error) return { error: tokens.error };
  const { flags, switches } = tokens;
  if (switches.has('--help')) return { help: true };
  const one = (flag) => flags.get(flag)?.[0];

  const baseUrl = one('--base-url');
  if (!baseUrl) return { error: '--base-url is required.' };
  if (!URL.canParse(baseUrl)) return { error: `--base-url is not a URL: ${baseUrl}` };
  const routes = one('--routes');
  if (!routes) return { error: '--routes is required.' };

  const languages = one('--langs') === undefined ? [...DEFAULT_LANGUAGES] : list(one('--langs'));
  const unsupported = languages.filter((language) => !SUPPORTED_LANGUAGES.includes(language));
  if (languages.length === 0 || unsupported.length > 0) return { error: `--langs takes ${SUPPORTED_LANGUAGES.join(', ')}; got ${one('--langs')}.` };

  const widths = [];
  for (const raw of one('--widths') === undefined ? DEFAULT_WIDTHS.map(String) : list(one('--widths'))) {
    const width = wholeNumber(raw, '--widths', MIN_WIDTH);
    if (width.error || width.value > MAX_WIDTH) return { error: `--widths takes whole pixel widths from ${MIN_WIDTH} to ${MAX_WIDTH}; got ${raw}.` };
    widths.push(width.value);
  }
  if (widths.length === 0) return { error: '--widths is empty.' };

  const target = one('--target') ?? null;
  const mode = target === null ? Modes.Baseline : Modes.Target;
  const update = switches.has('--update');
  const baselineDir = one('--baseline-dir') ?? null;
  if (mode === Modes.Baseline && baselineDir === null) return { error: '--baseline-dir is required without --target.' };
  if (mode === Modes.Target && update) return { error: '--update applies to baselines only, not to --target.' };

  const threshold = ratio(one('--threshold') ?? String(mode === Modes.Target ? TARGET_MAX_DIFF_RATIO : BASELINE_MAX_DIFF_RATIO), '--threshold');
  if (threshold.error) return threshold;
  const pixelThreshold = ratio(one('--pixel-threshold') ?? String(PIXEL_THRESHOLD), '--pixel-threshold');
  if (pixelThreshold.error) return pixelThreshold;
  const settle = wholeNumber(one('--settle-ms') ?? String(DEFAULT_SETTLE_MS), '--settle-ms', 0);
  if (settle.error) return settle;
  const iteration = one('--iteration') === undefined ? { value: null } : wholeNumber(one('--iteration'), '--iteration', FIRST_ITERATION);
  if (iteration.error) return iteration;

  const stamp = now.toISOString().replace(TIMESTAMP_UNSAFE, '-');
  return {
    options: {
      mode,
      baseUrl,
      routes,
      languages,
      widths,
      baselineDir: baselineDir === null ? null : path.resolve(baselineDir),
      update,
      target: target === null ? null : path.resolve(target),
      maxDiffRatio: threshold.value,
      pixelThreshold: pixelThreshold.value,
      maskSelectors: [...DEFAULT_MASK_SELECTORS, ...(flags.get('--mask') ?? [])],
      outDir: path.resolve(one('--out-dir') ?? path.join(tempDir, DEFAULT_OUTPUT_FOLDER, stamp)),
      settleMs: settle.value,
      /** null: the routes file's languageStorageKey, else DEFAULT_LANGUAGE_STORAGE_KEY (see resolveLanguageStorageKey). */
      languageStorageKey: one('--language-storage-key') ?? null,
      iteration: iteration.value,
      label: one('--label') ?? null,
      axe: !switches.has('--no-axe'),
    },
  };
}
