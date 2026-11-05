// Command-line parsing shared by the codelab-ng-tools subcommands, and their exit codes.

/** Exit codes of every subcommand: 0 clean, 1 a finding, 2 a usage or input error. */
export const ExitCodes = Object.freeze({ ok: 0, failure: 1, usage: 2 });

const OPTION_PREFIX = '--';

/**
 * Parses argv against a specification.
 * @param {string[]} argv
 * @param {{ values?: string[], repeatable?: string[], flags?: string[], positionals?: number }} spec option names
 *   with their `--` prefix: `values` take one value (last one wins), `repeatable` collect every value into an array,
 *   `flags` take none; `positionals` is the most positional arguments allowed (default 0)
 * @returns {{ options: Record<string, string | string[] | boolean>, positionals: string[] } | { error: string }}
 *   option keys are the names without the prefix; repeatable options default to `[]`, flags to `false`
 */
export function parseArguments(argv, { values = [], repeatable = [], flags = [], positionals: maxPositionals = 0 } = {}) {
  const options = Object.fromEntries([
    ...repeatable.map((name) => [name.slice(OPTION_PREFIX.length), []]),
    ...flags.map((name) => [name.slice(OPTION_PREFIX.length), false]),
  ]);
  const positionals = [];
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    const key = argument.slice(OPTION_PREFIX.length);
    if (flags.includes(argument)) {
      options[key] = true;
      continue;
    }
    if (values.includes(argument) || repeatable.includes(argument)) {
      const value = argv[index + 1];
      if (value === undefined || value.startsWith(OPTION_PREFIX)) return { error: `${argument} needs a value.` };
      if (repeatable.includes(argument)) options[key].push(value);
      else options[key] = value;
      index++;
      continue;
    }
    if (argument.startsWith(OPTION_PREFIX) || positionals.length >= maxPositionals) return { error: `Unexpected argument: ${argument}` };
    positionals.push(argument);
  }
  return { options, positionals };
}

/** Splits comma-separated values of a repeatable option (`--keep a,b --keep c`) into one trimmed list. */
export function splitList(values) {
  return values.flatMap((value) => value.split(',')).map((value) => value.trim()).filter((value) => value !== '');
}
