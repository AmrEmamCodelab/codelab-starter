// @vitest-environment node
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { parseArgs, resolveLanguageStorageKey, tokenize } from '../lib/args.mjs';
import {
  BASELINE_MAX_DIFF_RATIO,
  DEFAULT_LANGUAGE_STORAGE_KEY,
  DEFAULT_SETTLE_MS,
  Modes,
  PIXEL_THRESHOLD,
  TARGET_MAX_DIFF_RATIO,
} from '../lib/constants.mjs';

const REQUIRED = ['--base-url', 'http://localhost:4280', '--routes', 'routes.json'];
const BASELINE = [...REQUIRED, '--baseline-dir', 'baselines'];
const FIXED = { tempDir: path.resolve('tmp'), now: new Date('2026-11-05T10:00:00.000Z') };

describe('parseArgs', () => {
  it('fills the baseline-mode defaults: ar,en x 1440,390, 0.1 % and pixelmatch 0.1', () => {
    const { options } = parseArgs(BASELINE, FIXED);
    expect(options).toMatchObject({
      mode: Modes.Baseline,
      languages: ['ar', 'en'],
      widths: [1440, 390],
      maxDiffRatio: BASELINE_MAX_DIFF_RATIO,
      pixelThreshold: PIXEL_THRESHOLD,
      update: false,
      axe: true,
      settleMs: DEFAULT_SETTLE_MS,
      maskSelectors: ['[data-visual-mask]'],
      baselineDir: path.resolve('baselines'),
      languageStorageKey: null,
      iteration: null,
    });
    expect(options.outDir).toBe(path.join(FIXED.tempDir, 'codelab-visual', '2026-11-05T10-00-00-000Z'));
  });

  it('switches to target mode with --target and the 5 % threshold', () => {
    const { options } = parseArgs([...REQUIRED, '--target', 'design/{lang}.{width}.png', '--iteration', '2'], FIXED);
    expect(options.mode).toBe(Modes.Target);
    expect(options.maxDiffRatio).toBe(TARGET_MAX_DIFF_RATIO);
    expect(options.target).toBe(path.resolve('design/{lang}.{width}.png'));
    expect(options.iteration).toBe(2);
  });

  it('reads lists, repeatable masks, switches and overrides', () => {
    const { options } = parseArgs(
      [...BASELINE, '--langs', 'en', '--widths', '390', '--mask', '.clock', '--mask', '#counter', '--update', '--no-axe', '--threshold', '0.01', '--pixel-threshold', '0.2', '--settle-ms', '0', '--label', 'login'],
      FIXED,
    );
    expect(options).toMatchObject({
      languages: ['en'],
      widths: [390],
      maskSelectors: ['[data-visual-mask]', '.clock', '#counter'],
      update: true,
      axe: false,
      maxDiffRatio: 0.01,
      pixelThreshold: 0.2,
      settleMs: 0,
      label: 'login',
    });
  });

  it.each([
    [['--routes', 'r.json', '--baseline-dir', 'b'], '--base-url is required.'],
    [['--base-url', 'nope', '--routes', 'r.json', '--baseline-dir', 'b'], '--base-url is not a URL: nope'],
    [['--base-url', 'http://x'], '--routes is required.'],
    [REQUIRED, '--baseline-dir is required without --target.'],
    [[...REQUIRED, '--target', 't.png', '--update'], '--update applies to baselines only, not to --target.'],
    [[...BASELINE, '--langs', 'fr'], '--langs takes ar, en; got fr.'],
    [[...BASELINE, '--widths', '100'], '--widths takes whole pixel widths from 240 to 3840; got 100.'],
    [[...BASELINE, '--threshold', '2'], '--threshold must be a number from 0 to 1.'],
    [[...BASELINE, '--iteration', '0'], '--iteration must be a whole number of at least 1.'],
    [[...BASELINE, '--bogus'], 'Unknown option: --bogus'],
    [[...BASELINE, '--langs'], '--langs needs a value.'],
    [[...BASELINE, '--langs', '--update'], '--langs needs a value.'],
    [[...BASELINE, '--routes', 'again.json'], '--routes is given twice.'],
  ])('refuses %j', (argv, message) => {
    expect(parseArgs(argv, FIXED)).toEqual({ error: message });
  });

  it('answers --help before checking anything else', () => {
    expect(parseArgs(['--help'])).toEqual({ help: true });
  });
});

describe('tokenize', () => {
  it('collects values and switches', () => {
    const { flags, switches } = tokenize(['--mask', 'a', '--update', '--mask', 'b']);
    expect(flags.get('--mask')).toEqual(['a', 'b']);
    expect([...switches]).toEqual(['--update']);
  });
});

describe('resolveLanguageStorageKey', () => {
  it('prefers the command line, then the routes file, then the app key', () => {
    expect(resolveLanguageStorageKey('cli', 'file')).toBe('cli');
    expect(resolveLanguageStorageKey(null, 'file')).toBe('file');
    expect(resolveLanguageStorageKey(null, null)).toBe(DEFAULT_LANGUAGE_STORAGE_KEY);
  });
});
