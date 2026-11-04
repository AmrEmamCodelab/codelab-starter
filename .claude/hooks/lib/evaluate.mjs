// Evaluates forbidden/required rules against WHOLE file contents and reports only violations a change introduces.
// Ratchet semantics: a forbidden rule is violated when the new text has MORE matches than the old text; a required
// rule is violated when the new text lacks the pattern and the old text had it (or the file is new). A file that
// already broke a rule can therefore still be edited, but no edit may make it worse. CI checks absolute state.
import { matchesAnyGlob } from './glob.mjs';
import { RULE_KINDS } from './compiled-rules.mjs';

const GLOBAL_FLAG = 'g';

function countMatches(rule, text) {
  if (text === null || text === undefined) return 0;
  const flags = rule.flags.includes(GLOBAL_FLAG) ? rule.flags : `${rule.flags}${GLOBAL_FLAG}`;
  return [...text.matchAll(new RegExp(rule.pattern, flags))].length;
}

function appliesToPath(rule, relativePath) {
  return matchesAnyGlob(relativePath, rule.files) && !matchesAnyGlob(relativePath, rule.exclude);
}

function isIntroducedBy(rule, { before, after }) {
  if (rule.kind === RULE_KINDS.Forbidden) return countMatches(rule, after) > countMatches(rule, before);
  const isNewFile = before === null || before === undefined;
  const hadPattern = !isNewFile && countMatches(rule, before) > 0;
  return countMatches(rule, after) === 0 && (isNewFile || hadPattern);
}

/**
 * Rules from `rules` that apply to `relativePath` and that the change before→after newly violates.
 * `before` is null for a new file.
 */
export function findIntroducedViolations({ rules, relativePath, before, after }) {
  return rules.filter((rule) => appliesToPath(rule, relativePath) && isIntroducedBy(rule, { before, after }));
}

/**
 * Absolute state (what CI checks): rules from `rules` that apply to `relativePath` and that `text` breaks now.
 * A forbidden rule is broken by any match; a required rule by the absence of its pattern.
 */
export function findViolations({ rules, relativePath, text }) {
  return rules.filter((rule) => appliesToPath(rule, relativePath)
    && (rule.kind === RULE_KINDS.Forbidden ? countMatches(rule, text) > 0 : countMatches(rule, text) === 0));
}

/** One line per violation: rule id, message and fix (the refusal text Claude sees). */
export function formatViolations(relativePath, violations) {
  const lines = violations.map((rule) => {
    const message = rule.message ? ` ${rule.message}` : '';
    const fix = rule.fix ? ` Fix: ${rule.fix}` : '';
    return `- [${rule.id}]${message}${fix}`;
  });
  return [`${relativePath} breaks ${violations.length} rule(s) (whole-file check):`, ...lines].join('\n');
}
