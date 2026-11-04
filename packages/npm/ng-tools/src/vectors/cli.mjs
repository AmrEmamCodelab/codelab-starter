// The codelab-vectors-sync command line:
//
//   codelab-vectors-sync --from <dir> --to <dir> [--check] [--prune]
//
// Without --check it copies <from> into <to> (with --prune it also deletes files only in <to>). With --check it writes
// nothing and exits 1 when <to> differs from <from> in any way. Exit codes: 0 in sync / synced, 1 drift, 2 usage.

import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { compareFolders, syncFolders } from './sync.mjs';

export const ExitCodes = Object.freeze({ ok: 0, drift: 1, usage: 2 });

const VALUE_OPTIONS = Object.freeze(['--from', '--to']);
const FLAG_OPTIONS = Object.freeze(['--check', '--prune']);
const OPTION_PREFIX = '--';
export const USAGE = 'Usage: codelab-vectors-sync --from <dir> --to <dir> [--check] [--prune]';

/** Parses argv (without node and the script) into options, or returns an error message. */
export function parseArguments(argv) {
  const options = { check: false, prune: false };
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    if (FLAG_OPTIONS.includes(argument)) {
      options[argument.slice(OPTION_PREFIX.length)] = true;
    } else if (VALUE_OPTIONS.includes(argument) && index + 1 < argv.length && !argv[index + 1].startsWith(OPTION_PREFIX)) {
      options[argument.slice(OPTION_PREFIX.length)] = argv[++index];
    } else {
      return { error: `Unexpected argument: ${argument}` };
    }
  }
  if (options.from === undefined || options.to === undefined) return { error: '--from and --to are required.' };
  if (options.check && options.prune) return { error: '--check writes nothing, so it cannot be combined with --prune.' };
  return { options };
}

/** Runs the command; returns the exit code. `io` carries `log` and `error` writers. */
export function main(argv, io = console) {
  const parsed = parseArguments(argv);
  if (parsed.error !== undefined) {
    io.error(`${parsed.error}\n${USAGE}`);
    return ExitCodes.usage;
  }
  const from = resolve(parsed.options.from);
  const to = resolve(parsed.options.to);
  if (!existsSync(from)) {
    io.error(`Vector source folder not found: ${from}`);
    return ExitCodes.usage;
  }
  return parsed.options.check ? check({ from, to }, io) : sync({ from, to, prune: parsed.options.prune }, io);
}

function check(folders, io) {
  const drift = compareFolders(folders);
  for (const item of drift) io.error(`${item.kind}: ${item.path}`);
  if (drift.length > 0) {
    io.error(`Vectors in ${folders.to} drifted from ${folders.from} (${drift.length} file(s)). Run codelab-vectors-sync without --check.`);
    return ExitCodes.drift;
  }
  io.log(`Vectors in ${folders.to} match ${folders.from}.`);
  return ExitCodes.ok;
}

function sync(options, io) {
  const result = syncFolders(options);
  for (const path of result.copied) io.log(`copied: ${path}`);
  for (const path of result.removed) io.log(`removed: ${path}`);
  for (const path of result.extra) io.error(`extra (kept; use --prune to delete): ${path}`);
  io.log(`Synced ${result.copied.length} file(s) into ${options.to}.`);
  return ExitCodes.ok;
}
