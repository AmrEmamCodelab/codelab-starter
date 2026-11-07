// Loads and validates the three configuration files: facts-drift.config.json, stale-terms.txt and allowlist.txt.
import { readFileSync } from 'node:fs';
import { parseTermRules } from '../../extract/lib/rule-files.mjs';
import { STALE_RULE_PREFIX, SCRUB_TERM_RULE_PREFIX } from './constants.mjs';

const COMMENT_MARKER = '#';
const ALLOWLIST_FIELD_SEPARATOR = ' | ';
const ALLOWLIST_FIELD_COUNT = 4;
const WILDCARD = '*';
const REGEX_SPECIALS = /[.+?^${}()|[\]\\]/g;

/** Configuration keys every config file must define (dot paths), so a typo fails loudly instead of checking less. */
const REQUIRED_KEYS = Object.freeze([
  'scope.include', 'scope.exclude', 'paths.extensions', 'commands.shellFenceLanguages',
  'commands.powershellCommonParameters', 'types.sourceRoots', 'types.sourceExtensions', 'types.excludedDirectories',
  'types.suffixes', 'types.extraTypeNames', 'types.externalSymbols', 'references.externalSkills',
  'references.externalAgents', 'references.skillColumnHeaders', 'references.agentColumnHeaders',
]);

function valueAt(object, dottedKey) {
  return dottedKey.split('.').reduce((current, key) => (current == null ? undefined : current[key]), object);
}

/**
 * Parses facts-drift.config.json text. Every list in REQUIRED_KEYS must be an array of strings.
 * @param {string} text JSON
 * @param {string} sourceName used in error messages
 */
export function parseConfig(text, sourceName) {
  let raw;
  try {
    raw = JSON.parse(text);
  } catch (error) {
    throw new Error(`${sourceName}: invalid JSON: ${error.message}`);
  }
  for (const key of REQUIRED_KEYS) {
    const value = valueAt(raw, key);
    if (!Array.isArray(value) || value.some((item) => typeof item !== 'string')) {
      throw new Error(`${sourceName}: "${key}" must be an array of strings`);
    }
  }
  return {
    scope: { include: raw.scope.include, exclude: raw.scope.exclude },
    pathExtensions: new Set(raw.paths.extensions.map((extension) => extension.toLowerCase())),
    shellFenceLanguages: new Set(raw.commands.shellFenceLanguages.map((language) => language.toLowerCase())),
    powershellCommonParameters: new Set(raw.commands.powershellCommonParameters.map((name) => name.toLowerCase())),
    types: {
      sourceRoots: raw.types.sourceRoots,
      sourceExtensions: new Set(raw.types.sourceExtensions.map((extension) => extension.toLowerCase())),
      excludedDirectories: new Set(raw.types.excludedDirectories),
      suffixes: raw.types.suffixes,
      extraTypeNames: new Set(raw.types.extraTypeNames),
      externalSymbols: new Set(raw.types.externalSymbols),
    },
    references: {
      externalSkills: new Set(raw.references.externalSkills),
      externalAgents: new Set(raw.references.externalAgents),
      skillColumnHeaders: new Set(raw.references.skillColumnHeaders.map((header) => header.toLowerCase())),
      agentColumnHeaders: new Set(raw.references.agentColumnHeaders.map((header) => header.toLowerCase())),
    },
  };
}

/**
 * Parses stale-terms.txt with the scrub sweep's term parser (same "<id>[/<flags>]: <regex>" format) and renames the
 * rule ids to "stale-<id>".
 * @returns {{ id: string, pattern: RegExp, message: string }[]}
 */
export function parseStaleTerms(text, sourceName) {
  return parseTermRules(text, sourceName).map((rule) => ({
    id: `${STALE_RULE_PREFIX}${rule.id.slice(SCRUB_TERM_RULE_PREFIX.length)}`,
    pattern: rule.pattern,
    message: 'stale fact: this text describes the platform as it used to be (see tools/facts-drift/stale-terms.txt)',
  }));
}

/**
 * Parses allowlist.txt: "<rule id> | <file glob> | <subject> | <reason>" per line; the reason is required.
 * @returns {{ ruleId: string, fileGlob: string, subject: string, reason: string, line: number }[]}
 */
export function parseAllowlist(text, sourceName) {
  const entries = [];
  text.split(/\r?\n/).forEach((rawLine, index) => {
    const line = rawLine.trim();
    if (line.length === 0 || line.startsWith(COMMENT_MARKER)) return;
    const fields = line.split(ALLOWLIST_FIELD_SEPARATOR).map((field) => field.trim());
    const lineNumber = index + 1;
    if (fields.length < ALLOWLIST_FIELD_COUNT || fields.slice(0, ALLOWLIST_FIELD_COUNT).some((field) => field === '')) {
      throw new Error(`${sourceName}:${lineNumber}: expected "<rule id> | <file glob> | <subject> | <reason>", got "${line}"`);
    }
    const [ruleId, fileGlob, subject, ...reasonParts] = fields;
    entries.push({ ruleId, fileGlob, subject, reason: reasonParts.join(ALLOWLIST_FIELD_SEPARATOR), line: lineNumber });
  });
  return entries;
}

/** True when an allowlist rule-id selector ("x", "x*" or "*") selects the rule id. */
export function ruleIdSelected(selector, ruleId) {
  if (selector === WILDCARD) return true;
  if (selector.endsWith(WILDCARD)) return ruleId.startsWith(selector.slice(0, -WILDCARD.length));
  return selector === ruleId;
}

/**
 * True when an allowlist subject selects the finding's subject: "*" selects any, otherwise the text must match
 * exactly, where each "*" in the selector stands for any run of characters ("*appsettings.Development.local.json").
 */
export function subjectSelected(selector, subject) {
  if (selector === WILDCARD) return true;
  if (!selector.includes(WILDCARD)) return selector === subject;
  const pattern = selector.split(WILDCARD).map((part) => part.replace(REGEX_SPECIALS, '\\$&')).join('.*');
  return new RegExp(`^${pattern}$`).test(subject);
}

/** Reads and parses all three files. */
export function loadConfiguration({ configFile, staleTermsFile, allowlistFile }) {
  return {
    config: parseConfig(readFileSync(configFile, 'utf8'), configFile),
    staleTerms: parseStaleTerms(readFileSync(staleTermsFile, 'utf8'), staleTermsFile),
    allowlist: parseAllowlist(readFileSync(allowlistFile, 'utf8'), allowlistFile),
  };
}
