// Command-line parsing for check.mjs (kept apart so tests can call it without running the gate).
import { existsSync, statSync } from 'node:fs';
import { resolve } from 'node:path';
import { parseArgs, UsageError } from '../../extract/lib/cli-args.mjs';
import { DEFAULT_ALLOWLIST_FILE, DEFAULT_CONFIG_FILE, DEFAULT_ROOT, DEFAULT_STALE_TERMS_FILE } from './constants.mjs';

const KNOWN_OPTIONS = new Set(['root', 'json', 'config', 'stale-terms', 'allowlist']);
const VALUE_OPTIONS = ['root', 'config', 'stale-terms', 'allowlist'];

function optionValue(options, name, fallback) {
  const value = options[name];
  if (value === undefined) return fallback;
  if (typeof value !== 'string') throw new UsageError(`--${name} needs a value`);
  return value;
}

/** Parses argv into resolved paths and flags; throws UsageError on a mistake. */
export function parseCommandLine(argv) {
  const { positionals, options } = parseArgs(argv);
  if (positionals.length > 0) throw new UsageError(`unexpected argument '${positionals[0]}'`);
  for (const name of Object.keys(options)) {
    if (!KNOWN_OPTIONS.has(name)) throw new UsageError(`unknown option --${name}`);
  }
  if (options.json !== undefined && options.json !== true) throw new UsageError('--json takes no value');
  for (const name of VALUE_OPTIONS) optionValue(options, name, null);
  const root = resolve(optionValue(options, 'root', DEFAULT_ROOT));
  if (!existsSync(root) || !statSync(root).isDirectory()) throw new UsageError(`--root '${root}' is not a folder`);
  const files = {
    configFile: resolve(optionValue(options, 'config', DEFAULT_CONFIG_FILE)),
    staleTermsFile: resolve(optionValue(options, 'stale-terms', DEFAULT_STALE_TERMS_FILE)),
    allowlistFile: resolve(optionValue(options, 'allowlist', DEFAULT_ALLOWLIST_FILE)),
  };
  for (const [name, file] of Object.entries(files)) {
    if (!existsSync(file)) throw new UsageError(`${name} '${file}' does not exist`);
  }
  return { root, json: options.json === true, ...files };
}
