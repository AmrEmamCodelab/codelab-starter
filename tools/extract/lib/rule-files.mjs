// Parsers for the two text configuration files of the scrub sweep: the term list and the exceptions list.
import { matchesGlob } from './glob.mjs';

export const TERM_RULE_PREFIX = 'term-';
const COMMENT_MARKER = '#';
const CASE_INSENSITIVE_FLAG = 'i';
const ALLOWED_TERM_FLAGS = new Set([CASE_INSENSITIVE_FLAG]);
const TERM_LINE = /^([a-z0-9][a-z0-9-]*)(?:\/([a-z]+))?:\s+(.+)$/;
const EXCEPTION_LINE = /^(\S+)\s+(\S+)$/;
const RULE_ID_WILDCARD = '*';
const RULE_ID_SEPARATOR = ',';

function meaningfulLines(text) {
  return text
    .split(/\r?\n/)
    .map((raw, index) => ({ raw: raw.trim(), number: index + 1 }))
    .filter(({ raw }) => raw.length > 0 && !raw.startsWith(COMMENT_MARKER));
}

/**
 * Parses the term list ("<id>[/<flags>]: <regex>" per line) into scrub rules.
 * @param {string} text file content
 * @param {string} sourceName used in error messages
 * @returns {{ id: string, pattern: RegExp, message: string }[]}
 */
export function parseTermRules(text, sourceName) {
  return meaningfulLines(text).map(({ raw, number }) => {
    const match = TERM_LINE.exec(raw);
    if (!match) throw new Error(`${sourceName}:${number}: expected "<id>[/<flags>]: <regex>", got "${raw}"`);
    const [, id, flags = '', source] = match;
    for (const flag of flags) {
      if (!ALLOWED_TERM_FLAGS.has(flag)) throw new Error(`${sourceName}:${number}: unsupported flag "${flag}"`);
    }
    let pattern;
    try {
      pattern = new RegExp(source, `g${flags}`);
    } catch (error) {
      throw new Error(`${sourceName}:${number}: invalid regex: ${error.message}`);
    }
    return { id: `${TERM_RULE_PREFIX}${id}`, pattern, message: 'source-product term (see tools/extract/modhs-terms.txt)' };
  });
}

/**
 * Parses the exceptions list ("<glob> <ruleIds>" per line).
 * @returns {{ glob: string, ruleIds: string[] }[]}
 */
export function parseExceptions(text, sourceName) {
  return meaningfulLines(text).map(({ raw, number }) => {
    const match = EXCEPTION_LINE.exec(raw);
    if (!match) throw new Error(`${sourceName}:${number}: expected "<glob> <ruleIds>", got "${raw}"`);
    return { glob: match[1], ruleIds: match[2].split(RULE_ID_SEPARATOR).filter(Boolean) };
  });
}

function ruleIdMatches(selector, ruleId) {
  if (selector === RULE_ID_WILDCARD) return true;
  if (selector.endsWith(RULE_ID_WILDCARD)) return ruleId.startsWith(selector.slice(0, -RULE_ID_WILDCARD.length));
  return selector === ruleId;
}

/** True when an exception entry allows ruleId to match in path. */
export function isExcepted(exceptions, path, ruleId) {
  return exceptions.some((entry) => matchesGlob(path, entry.glob)
    && entry.ruleIds.some((selector) => ruleIdMatches(selector, ruleId)));
}
