// The `codelab-ng-tools` command line. Subcommands:
//
//   codelab-ng-tools permission-gates --dir <folder> [--fix <text>]
//
// permission-gates checks every action button in the templates under <folder> (see check.mjs); `--fix` replaces the
// fix text (a project points it at its recipe). Exit codes: 0 every button gated, 1 a finding, 2 usage.
import path from 'node:path';
import { checkPermissionGates, formatGateFindings } from './check.mjs';

export const ExitCodes = Object.freeze({ ok: 0, failure: 1, usage: 2 });
export const Subcommands = Object.freeze({ permissionGates: 'permission-gates' });

const Options = Object.freeze({ dir: '--dir', fix: '--fix' });
const OPTION_PREFIX = '--';
const VALUE_OPTIONS = new Set(Object.values(Options));
export const USAGE = `Usage: codelab-ng-tools ${Subcommands.permissionGates} ${Options.dir} <folder> [${Options.fix} <text>]`;

/** Parses the permission-gates arguments; returns `{ options }` or `{ error }`. */
export function parsePermissionGateArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    const value = argv[index + 1];
    if (!VALUE_OPTIONS.has(argument) || value === undefined || value.startsWith(OPTION_PREFIX)) {
      return { error: `Unexpected argument: ${argument}` };
    }
    options[argument.slice(OPTION_PREFIX.length)] = value;
    index++;
  }
  if (options.dir === undefined) return { error: `${Options.dir} is required.` };
  return { options };
}

/** Runs the permission-gates check; returns the exit code. */
export function runPermissionGates(options, io = console) {
  const result = checkPermissionGates(path.resolve(options.dir));
  if (result.error !== undefined) {
    io.error(result.error);
    return ExitCodes.usage;
  }
  if (result.findings.length > 0) {
    io.error(formatGateFindings(result.findings, options.fix));
    return ExitCodes.failure;
  }
  io.log(`permission gates: ${result.fileCount} file(s), every action button gated`);
  return ExitCodes.ok;
}

/** Dispatches argv (without node and the script) to a subcommand; returns the exit code. */
export function main(argv, io = console) {
  const [subcommand, ...rest] = argv;
  if (subcommand !== Subcommands.permissionGates) {
    io.error(`Unknown subcommand: ${subcommand ?? '(none)'}\n${USAGE}`);
    return ExitCodes.usage;
  }
  const parsed = parsePermissionGateArguments(rest);
  if (parsed.error !== undefined) {
    io.error(`${parsed.error}\n${USAGE}`);
    return ExitCodes.usage;
  }
  return runPermissionGates(parsed.options, io);
}
