// A small argv parser for the fork CLIs: --name value, --switch, and repeatable --name value lists.

/** Option kinds. */
export const OptionKind = Object.freeze({ string: 'string', boolean: 'boolean', list: 'list' });

const FLAG_PREFIX = '--';
const WORD_BOUNDARY = /-([a-z])/g;

/** Raised for an unknown flag, a missing value or a missing required option. */
export class UsageError extends Error {}

function toKey(flag) {
  return flag.slice(FLAG_PREFIX.length).replace(WORD_BOUNDARY, (_match, letter) => letter.toUpperCase());
}

function defaults(spec) {
  const options = {};
  for (const [flag, kind] of Object.entries(spec)) {
    const key = toKey(flag);
    if (kind === OptionKind.boolean) options[key] = false;
    else if (kind === OptionKind.list) options[key] = [];
    else options[key] = undefined;
  }
  return options;
}

/**
 * Parses argv against a spec of { '--flag-name': OptionKind }. Keys come back camelCased (--feed-dir -> feedDir).
 * @param {string[]} argv
 * @param {Record<string, string>} spec
 * @param {string[]} [required] flags that must be present
 */
export function parseOptions(argv, spec, required = []) {
  const options = defaults(spec);
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    const kind = spec[flag];
    if (kind === undefined) throw new UsageError(`Unknown argument '${flag}'.`);
    if (kind === OptionKind.boolean) {
      options[toKey(flag)] = true;
      continue;
    }
    const value = argv[index + 1];
    if (value === undefined || value.startsWith(FLAG_PREFIX)) throw new UsageError(`${flag} needs a value.`);
    if (kind === OptionKind.list) options[toKey(flag)].push(value);
    else options[toKey(flag)] = value;
    index += 1;
  }
  for (const flag of required) {
    if (options[toKey(flag)] === undefined) throw new UsageError(`${flag} is required.`);
  }
  return options;
}
