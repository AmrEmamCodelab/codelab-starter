// Argument parsing and the exit-code wrapper shared by the scaffold CLIs.
import path from 'node:path';
import { ExitCode, Flags } from './constants.mjs';
import { ScaffoldRefusal } from './csharp-edit.mjs';
import { DEFAULT_ROOT, GeneratorError } from './workspace.mjs';

/** Bad arguments (exit 2). */
export class UsageError extends Error {}

const BOOLEAN_FLAGS = new Set([Flags.skipGenerate, Flags.noGenerate, Flags.dryRun, Flags.help]);

/**
 * Parses `--flag value` pairs and boolean switches. Only `allowed` flags are accepted.
 * @param {string[]} argv
 * @param {string[]} allowed value flags this CLI takes (besides --root, --skip-generate, --no-gen, --dry-run and --help)
 * @returns {Record<string, string | boolean> & { root: string }}
 */
export function parseArguments(argv, allowed) {
  const values = { root: DEFAULT_ROOT };
  const accepted = new Set([...allowed, Flags.root]);
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    if (BOOLEAN_FLAGS.has(flag)) {
      values[flag.slice(2)] = true;
      continue;
    }
    const value = argv[index + 1];
    if (!accepted.has(flag)) throw new UsageError(`Unknown argument '${flag}'.`);
    if (value === undefined || value.startsWith('--')) throw new UsageError(`${flag} needs a value.`);
    values[flag.slice(2)] = flag === Flags.root ? path.resolve(value) : value;
    index += 1;
  }
  return values;
}

/**
 * Runs a CLI body and maps its outcome to the exit code: 0 done, 1 refused (nothing changed), 2 usage or failure.
 * @param {string} usage the usage text printed for --help and usage errors
 * @param {() => string[]} body returns the report lines to print
 */
export function runCli(usage, body, io = { out: console.log, err: console.error }) {
  try {
    for (const line of body()) io.out(line);
    return ExitCode.ok;
  } catch (error) {
    if (error instanceof ScaffoldRefusal) {
      io.err(`refused: ${error.message}`);
      return ExitCode.refused;
    }
    if (error instanceof UsageError) {
      io.err(`${error.message}\n${usage}`);
      return ExitCode.usage;
    }
    if (error instanceof GeneratorError) {
      io.err(`The files were written, but a downstream generator failed: ${error.message}`);
      return ExitCode.usage;
    }
    throw error;
  }
}
