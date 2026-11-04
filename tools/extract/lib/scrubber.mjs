// Applies scrub rules (terms + built-ins) to file paths and contents, honouring the exceptions list.
import { readFileSync } from 'node:fs';
import { basename } from 'node:path';
import { listFiles, readScannable } from './files.mjs';
import { createFinding, PATH_FINDING_LINE, splitLines, unscannableFinding, UNSCANNABLE_RULE_ID } from './findings.mjs';
import { isExcepted, parseExceptions, parseTermRules } from './rule-files.mjs';
import { builtInScrubRules, SCOPE_BOTH, SCOPE_CONTENT, SCOPE_PATH } from './scrub-rules.mjs';
import { normalizeForTerms } from './term-text.mjs';

const PATH_COLUMN = 1;

/**
 * Loads the full rule set: term rules (scope both: content and file names) plus the built-in rules.
 * @param {{ termsFile: string, exceptionsFile: string }} files absolute paths
 */
export function loadScrubConfiguration({ termsFile, exceptionsFile }) {
  // The term list's own file name is how docs and code point at it, so a reference to it is never a finding.
  const ignoredLiterals = [basename(termsFile)];
  const termRules = parseTermRules(readFileSync(termsFile, 'utf8'), termsFile)
    .map((rule) => ({ ...rule, scope: SCOPE_BOTH, ignoredLiterals, matchNormalized: true }));
  return {
    rules: [...termRules, ...builtInScrubRules()],
    exceptions: parseExceptions(readFileSync(exceptionsFile, 'utf8'), exceptionsFile),
  };
}

function ruleAppliesTo(rule, path, scope) {
  if (rule.scope !== SCOPE_BOTH && rule.scope !== scope) return false;
  return rule.appliesTo ? rule.appliesTo(path) : true;
}

function ignoredRanges(literals, lineText) {
  const ranges = [];
  for (const literal of literals ?? []) {
    for (let start = lineText.indexOf(literal); start !== -1; start = lineText.indexOf(literal, start + 1)) {
      ranges.push({ start, end: start + literal.length });
    }
  }
  return ranges;
}

/**
 * Term rules also match the normalized line (identifiers split, Arabic normalized; see term-text.mjs). A match there
 * is reported at its original index unless the plain line already matched at that index.
 */
function normalizedMatches(rule, lineText, plainIndexes) {
  const { text, originalIndex } = normalizeForTerms(lineText);
  if (text === lineText) return [];
  return [...text.matchAll(rule.pattern)]
    .map((match) => ({ 0: match[0], index: originalIndex[match.index] ?? 0 }))
    .filter((match) => !plainIndexes.has(match.index));
}

function matchesInLine(rule, lineText) {
  const ignored = ignoredRanges(rule.ignoredLiterals, lineText);
  const plain = [...lineText.matchAll(rule.pattern)];
  const all = rule.matchNormalized
    ? [...plain, ...normalizedMatches(rule, lineText, new Set(plain.map((match) => match.index)))]
    : plain;
  return all
    .filter((match) => !ignored.some((range) => match.index >= range.start && match.index < range.end))
    .filter((match) => !(rule.isAllowedMatch?.(match[0], lineText, match.index)));
}

function excerptFor(rule, lineText, matchText) {
  return rule.mask ? lineText.replace(matchText, rule.mask(matchText)).trim() : lineText.trim();
}

/**
 * Scans one file's path and text.
 * @param {string} path repo-relative forward-slash path (used for exceptions and file-type rules)
 * @param {string | null} text file content, or null for a binary file (only the path is scanned)
 * @param {{ rules: object[], exceptions: object[] }} configuration
 */
export function scrubFile(path, text, { rules, exceptions }) {
  const active = rules.filter((rule) => !isExcepted(exceptions, path, rule.id));
  const findings = [];
  for (const rule of active.filter((candidate) => ruleAppliesTo(candidate, path, SCOPE_PATH))) {
    for (const match of matchesInLine(rule, path)) {
      findings.push(createFinding({
        path, line: PATH_FINDING_LINE, column: PATH_COLUMN, ruleId: rule.id, message: rule.message, excerpt: match[0],
      }));
    }
  }
  if (text === null) return findings;
  const contentRules = active.filter((candidate) => ruleAppliesTo(candidate, path, SCOPE_CONTENT));
  for (const line of splitLines(text)) {
    for (const rule of contentRules) {
      for (const match of matchesInLine(rule, line.text)) {
        findings.push(createFinding({
          path,
          line: line.number,
          column: match.index + 1,
          ruleId: rule.id,
          message: rule.message,
          excerpt: excerptFor(rule, line.text, match[0]),
        }));
      }
    }
  }
  return findings;
}

function scrubItem({ path, text, unscannable }, configuration) {
  const findings = scrubFile(path, text, configuration);
  if (unscannable && !isExcepted(configuration.exceptions, path, UNSCANNABLE_RULE_ID)) {
    findings.push(unscannableFinding({ path, reason: unscannable }));
  }
  return findings;
}

/**
 * Scans every file under root (git-aware, see files.mjs): UTF-8 and UTF-16 text, and the entries of zip containers.
 * A binary that cannot be scanned is a finding unless its extension is allowlisted (images and fonts).
 * @returns {{ findings: object[], fileCount: number }}
 */
export function scrubTree(root, configuration) {
  const paths = listFiles(root);
  const findings = paths.flatMap((path) => readScannable(root, path).flatMap((item) => scrubItem(item, configuration)));
  return { findings, fileCount: paths.length };
}
