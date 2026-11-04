// Minimal "--name value" / "--flag" parser so the CLIs stay dependency-free.
import { EXIT_USAGE_OR_ERROR } from './exit-codes.mjs';

const OPTION_PREFIX = '--';

/** A command-line mistake: the CLI prints the message and exits with the usage exit code. */
export class UsageError extends Error {}

/**
 * Parses argv into { positionals, options }. A token "--x" followed by a non-option token takes it as its value;
 * otherwise "--x" is a boolean flag.
 * @param {string[]} argv the arguments after the script path
 */
export function parseArgs(argv) {
  const positionals = [];
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith(OPTION_PREFIX)) {
      positionals.push(token);
      continue;
    }
    const name = token.slice(OPTION_PREFIX.length);
    const next = argv[index + 1];
    if (next !== undefined && !next.startsWith(OPTION_PREFIX)) {
      options[name] = next;
      index += 1;
    } else {
      options[name] = true;
    }
  }
  return { positionals, options };
}

/** Returns options[name] as a string, or throws a UsageError naming the option. */
export function requireOption(options, name) {
  const value = options[name];
  if (typeof value !== 'string' || value.length === 0) {
    throw new UsageError(`missing required option ${OPTION_PREFIX}${name} <value>`);
  }
  return value;
}

/**
 * Runs a CLI body and turns its result into the process exit code. A UsageError or any other error prints one line
 * to stderr and exits with EXIT_USAGE_OR_ERROR, so a crash can never look like a clean scan.
 * @param {string} toolName prefix for error lines
 * @param {() => Promise<number> | number} body returns the exit code
 */
export async function runCli(toolName, body) {
  try {
    process.exitCode = await body();
  } catch (error) {
    const detail = error instanceof UsageError ? `usage: ${error.message}` : (error?.stack ?? String(error));
    console.error(`${toolName}: ${detail}`);
    process.exitCode = EXIT_USAGE_OR_ERROR;
  }
}
