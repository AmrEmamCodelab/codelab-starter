// Secret scan: regex rules plus the known-secret digest denylist (SHA-256 of source-product key material).
import { readFileSync } from 'node:fs';
import { listSecretScanFiles, readScannable } from './files.mjs';
import { createFinding, splitLines, unscannableFinding } from './findings.mjs';
import { sha256Hex } from './hashing.mjs';
import { digestsOf } from './key-material.mjs';
import { publicDigestSpellings } from './public-digests.mjs';
import { withoutExcepted } from './secret-exceptions.mjs';
import { redact, secretRules } from './secret-rules.mjs';

export const DIGEST_RULE_ID = 'known-secret-digest';
export const DIGEST_ALGORITHM = 'sha256';
/** Repo-relative path of the committed denylist. */
export const DIGEST_LIST_PATH = 'tools/extract/secret-digests.json';
// Shorter strings are too common to be distinctive key material.
export const MIN_CANDIDATE_LENGTH = 8;
const QUOTED_LITERAL = /"((?:[^"\\\r\n]|\\.)*)"|'([^'\r\n]*)'|`([^`\r\n]*)`/g;
const TOKEN_SEPARATORS = /[\s"'`]+/;
const ASSIGNMENT_OPERATOR = /[=:]/;
const TRAILING_PUNCTUATION = /[,;]+$/;
const PEM_BLOCK = /-----BEGIN [A-Z0-9 ]+-----([\s\S]*?)-----END [A-Z0-9 ]+-----/g;
const WHITESPACE = /\s+/g;
const PEM_HEADER_LINE = /^[A-Za-z-]+:.*$/gm;

/**
 * The canonical form of a PEM block for hashing: the base64 body without headers or whitespace, so a key matches
 * whatever line endings or wrapping it was copied with.
 */
export function normalizePemBody(body) {
  return body.replace(PEM_HEADER_LINE, '').replace(WHITESPACE, '');
}

function unescapeJsonString(raw) {
  try {
    return JSON.parse(`"${raw}"`);
  } catch {
    // Not a valid JSON escape sequence: the raw text is the only candidate.
    return raw;
  }
}

/**
 * Every substring of a line that could be a secret value: quoted literals (raw and JSON-unescaped), adjacent quoted
 * literals joined (a key split as "abc" + "def"), whitespace tokens, and the value part of "name=value" /
 * "name:value" tokens.
 * @param {string} lineText
 * @returns {Set<string>}
 */
export function candidateValues(lineText) {
  const candidates = new Set();
  const literals = [];
  for (const match of lineText.matchAll(QUOTED_LITERAL)) {
    const raw = match[1] ?? match[2] ?? match[3];
    literals.push(raw);
    candidates.add(raw);
    candidates.add(unescapeJsonString(raw));
  }
  for (let index = 1; index < literals.length; index += 1) candidates.add(literals[index - 1] + literals[index]);
  if (literals.length > 2) candidates.add(literals.join(''));
  for (const rawToken of lineText.split(TOKEN_SEPARATORS)) {
    const token = rawToken.replace(TRAILING_PUNCTUATION, '');
    candidates.add(token);
    const operator = token.search(ASSIGNMENT_OPERATOR);
    if (operator !== -1) candidates.add(token.slice(operator + 1));
  }
  return new Set([...candidates].filter((candidate) => candidate.length >= MIN_CANDIDATE_LENGTH));
}

/**
 * Loads the digest denylist file: { algorithm: "sha256", digests: [{ sha256, kind }] }.
 * @returns {Map<string, string>} digest -> kind
 */
export function loadDigestDenylist(digestFile) {
  const content = JSON.parse(readFileSync(digestFile, 'utf8'));
  if (content.algorithm !== DIGEST_ALGORITHM) throw new Error(`${digestFile}: algorithm must be ${DIGEST_ALGORITHM}`);
  return new Map(content.digests.map((entry) => [entry.sha256, entry.kind]));
}

function regexFindings(path, line, rules) {
  const findings = [];
  for (const rule of rules.filter((candidate) => !candidate.appliesTo || candidate.appliesTo(path))) {
    for (const match of line.text.matchAll(rule.pattern)) {
      if (rule.isAllowedMatch?.(match)) continue;
      const secret = match[rule.valueGroup ?? 0];
      findings.push(createFinding({
        path,
        line: line.number,
        column: match.index + 1,
        ruleId: rule.id,
        message: rule.message,
        excerpt: line.text.replace(secret, redact(secret)).trim(),
      }));
    }
  }
  return findings;
}

// The SHA-256 of a public input (the empty body) is never a secret, whatever fixture it once appeared in.
const PUBLIC_DIGESTS = publicDigestSpellings();

function digestFindings(path, line, digests) {
  const findings = [];
  for (const candidate of candidateValues(line.text)) {
    if (PUBLIC_DIGESTS.has(candidate)) continue;
    const kind = digestsOf(candidate).map((digest) => digests.get(digest)).find(Boolean);
    if (!kind) continue;
    findings.push(createFinding({
      path,
      line: line.number,
      column: Math.max(line.text.indexOf(candidate), 0) + 1,
      ruleId: DIGEST_RULE_ID,
      message: `known source-product secret (${kind}); generate a new value, never reuse it`,
      excerpt: redact(candidate),
    }));
  }
  return findings;
}

function pemDigestFindings(path, text, digests) {
  const findings = [];
  for (const match of text.matchAll(PEM_BLOCK)) {
    const kind = digests.get(sha256Hex(normalizePemBody(match[1])));
    if (!kind) continue;
    const lineNumber = text.slice(0, match.index).split('\n').length;
    findings.push(createFinding({
      path, line: lineNumber, column: 1, ruleId: DIGEST_RULE_ID,
      message: `known source-product key (${kind})`, excerpt: redact(match[0]),
    }));
  }
  return findings;
}

/**
 * Scans one file's text.
 * @param {string} path repo-relative path
 * @param {string} text
 * @param {Map<string, string>} digests from loadDigestDenylist
 */
export function scanTextForSecrets(path, text, digests) {
  const rules = secretRules();
  // The denylist holds SHA-256 digests (one-way); a digest that equals another listed value's digest is not a secret.
  const lineDigests = path === DIGEST_LIST_PATH ? new Map() : digests;
  const findings = [];
  for (const line of splitLines(text)) {
    findings.push(...regexFindings(path, line, rules), ...digestFindings(path, line, lineDigests));
  }
  findings.push(...pemDigestFindings(path, text, digests));
  return findings;
}

/**
 * Scans every file under root (git-aware, plus the gitignored .claude/memory; see listSecretScanFiles in files.mjs):
 * UTF-8 and UTF-16 text and the entries of zip containers.
 * A binary that cannot be scanned (not an allowlisted image or font) is a finding: a key must not hide in one.
 * @param {{ glob: string, ruleIds: string[] }[]} [exceptions] from loadSecretExceptions (never the digest rule)
 */
export function scanTreeForSecrets(root, digests, exceptions = []) {
  const paths = listSecretScanFiles(root);
  const findings = [];
  for (const item of paths.flatMap((path) => readScannable(root, path))) {
    if (item.text !== null) findings.push(...scanTextForSecrets(item.path, item.text, digests));
    else if (item.unscannable) findings.push(unscannableFinding({ path: item.path, reason: item.unscannable }));
  }
  return { findings: withoutExcepted(findings, exceptions), fileCount: paths.length };
}
