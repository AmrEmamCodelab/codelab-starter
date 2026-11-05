// `codelab-ng-tools i18n-check --dir <translations> [--src <folder>]... [--allow-latin <key>]... [--pipe <name>]...
//    [--fix <text>]`
//
// Checks the translation files in --dir (parity, ar and en present, no empty text, Arabic text in ar) and, for every
// --src folder, that each key its templates and TypeScript use literally exists (see key-usage.mjs). `--allow-latin`
// lists Arabic keys that are deliberately Latin (`key` or `group.*`, comma-separated or repeated); `--pipe` replaces
// the translate pipe names (default t, translate). Exit codes: 0 clean, 1 a problem, 2 usage.
import path from 'node:path';
import { ExitCodes, parseArguments, splitList } from '../shared/arguments.mjs';
import { checkI18n, formatI18nProblems } from './check.mjs';
import { DEFAULT_TRANSLATE_PIPES } from './key-usage.mjs';

export const I18nOptions = Object.freeze({ dir: '--dir', src: '--src', allowLatin: '--allow-latin', pipe: '--pipe', fix: '--fix' });
export const I18N_USAGE = `Usage: codelab-ng-tools i18n-check ${I18nOptions.dir} <translations> [${I18nOptions.src} <folder>]... [${I18nOptions.allowLatin} <key>]... [${I18nOptions.pipe} <name>]... [${I18nOptions.fix} <text>]`;

const SPEC = Object.freeze({
  values: [I18nOptions.dir, I18nOptions.fix],
  repeatable: [I18nOptions.src, I18nOptions.allowLatin, I18nOptions.pipe],
});

/** Parses the i18n-check arguments; returns the options or `{ error }`. */
export function parseI18nArguments(argv) {
  const parsed = parseArguments(argv, SPEC);
  if (parsed.error !== undefined) return parsed;
  const { dir, src, pipe, fix } = parsed.options;
  if (dir === undefined) return { error: `${I18nOptions.dir} is required.` };
  const pipes = splitList(pipe);
  return { dir, sourceDirs: src, allowLatin: splitList(parsed.options['allow-latin']), pipes: pipes.length > 0 ? pipes : [...DEFAULT_TRANSLATE_PIPES], fix };
}

/** Runs i18n-check with argv after the subcommand; returns the exit code. */
export function runI18nCheck(argv, io = console) {
  const options = parseI18nArguments(argv);
  if (options.error !== undefined) {
    io.error(`${options.error}\n${I18N_USAGE}`);
    return ExitCodes.usage;
  }
  const result = checkI18n({
    dir: path.resolve(options.dir),
    sourceDirs: options.sourceDirs.map((dir) => path.resolve(dir)),
    allowLatin: options.allowLatin,
    pipes: options.pipes,
  });
  if (result.problems.length > 0) {
    io.error(formatI18nProblems(result.problems, options.fix));
    return ExitCodes.failure;
  }
  io.log(`i18n check ok: ${result.languages.join(', ')}; ${result.usedKeyCount} key(s) used in code, all present`);
  return ExitCodes.ok;
}
