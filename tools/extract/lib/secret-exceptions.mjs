// Where secret-scan rules may match: tools/extract/secret-exceptions.txt. Same line format as the scrub exceptions
// ("<glob> <rule ids>"), but stricter, because a secret exception hides key material:
//   - every rule id is named exactly (no "*" or "prefix*"), and must be a regex rule of lib/secret-rules.mjs;
//   - the digest denylist (known-secret-digest) and unscannable binaries can never be excepted: a known source-product
//     key is a finding wherever it is, and a binary is never waved through;
//   - a glob must be anchored to a folder (no leading "*" or "**").
import { readFileSync } from 'node:fs';
import { UNSCANNABLE_RULE_ID } from './findings.mjs';
import { isExcepted, parseExceptions } from './rule-files.mjs';
import { secretRules } from './secret-rules.mjs';

export const NEVER_EXCEPTABLE = Object.freeze(['known-secret-digest', UNSCANNABLE_RULE_ID]);
const WILDCARD = '*';
const ANCHORED_GLOB = /^[^*]/;

/**
 * Parses and checks the exceptions text.
 * @param {string} text file content
 * @param {string} sourceName used in error messages
 * @returns {{ glob: string, ruleIds: string[] }[]}
 */
export function parseSecretExceptions(text, sourceName) {
  const known = new Set(secretRules().map((rule) => rule.id));
  const entries = parseExceptions(text, sourceName);
  for (const { glob, ruleIds } of entries) {
    if (!ANCHORED_GLOB.test(glob)) throw new Error(`${sourceName}: "${glob}" must start with a folder, not a wildcard`);
    for (const ruleId of ruleIds) {
      if (ruleId.includes(WILDCARD)) throw new Error(`${sourceName}: "${glob}" must name each rule exactly, not "${ruleId}"`);
      if (NEVER_EXCEPTABLE.includes(ruleId)) throw new Error(`${sourceName}: rule "${ruleId}" can never be excepted`);
      if (!known.has(ruleId)) throw new Error(`${sourceName}: "${ruleId}" is not a secret-scan rule`);
    }
  }
  return entries;
}

/** Reads the exceptions file; a missing file means no exceptions. */
export function loadSecretExceptions(path) {
  let text;
  try {
    text = readFileSync(path, 'utf8');
  } catch (error) {
    if (error.code === 'ENOENT') return [];
    throw error;
  }
  return parseSecretExceptions(text, path);
}

/** The findings no exception covers. */
export function withoutExcepted(findings, exceptions) {
  return findings.filter((finding) => !isExcepted(exceptions, finding.path, finding.ruleId));
}
