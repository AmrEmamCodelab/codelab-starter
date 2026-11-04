// Collects key-material VALUES from source-product files so their SHA-256 digests can be denylisted.
// Only digests leave this module's callers; the values themselves are never written anywhere.
import { normalizePemBody, MIN_CANDIDATE_LENGTH } from './secret-scanner.mjs';
import { isPlaceholder, secretRules } from './secret-rules.mjs';

export const KIND_CONFIG_SECRET = 'config-secret';
export const KIND_CONNECTION_STRING = 'connection-string';
export const KIND_VECTOR_KEY = 'vector-key';
export const KIND_TEST_FIXTURE = 'test-fixture';
export const KIND_SCRIPT_SECRET = 'script-secret';
// The secret scan's own rules find the values in scripts and YAML; they are read as if the file were a script.
const SCRIPT_RULE_PATH = 'source.ps1';
const LINE_BREAK = /\r?\n/;

// Setting names that hold secrets, and suffixes that mark a setting ABOUT a secret (its name, path, header...).
const SECRET_SETTING_NAME = /secret|password|pwd|apikey|signingkey|hashkey|ticketkey|material|privatekey|accesskey|connection/i;
const NON_SECRET_SETTING_SUFFIX = /(?:name|id|ids|path|header|endpoint|url|uri|seconds|limit|source|thumbprint|enabled|mode|until|lifetime|field|note)$/i;
const CONNECTION_STRINGS_SECTION = /^connectionstrings$/i;
const COMMENT_KEY_PREFIX = '//';
// Golden-vector fields that carry key material (keyHex, masterBase64, sessionKeyHex, materialBase64, macKeyHex...).
const VECTOR_KEY_FIELD = /(?:key|master|material)(?:hex|base64)$/i;
// Literals in test code that look like raw key material: base64 of >= 16 bytes, or >= 128-bit hex.
const FIXTURE_KEY_LITERAL = /^(?:[A-Za-z0-9+/_-]{22,}={0,2}|[0-9a-fA-F]{32,})$/;
const STRING_LITERAL = /"((?:[^"\\\r\n]|\\.)*)"|'([^'\\\r\n]*)'/g;
const PEM_MARKER = '-----BEGIN';
const BYTE_ORDER_MARK = String.fromCharCode(0xfeff);

function stripByteOrderMark(text) {
  return text.startsWith(BYTE_ORDER_MARK) ? text.slice(BYTE_ORDER_MARK.length) : text;
}
const PEM_BODY = /-----BEGIN [A-Z0-9 ]+-----([\s\S]*?)-----END [A-Z0-9 ]+-----/;

/**
 * Parses JSON that may carry // and /* *\/ comments and trailing commas (appsettings files do).
 * @param {string} text
 */
export function parseLenientJson(text) {
  text = stripByteOrderMark(text);
  let output = '';
  let inString = false;
  for (let index = 0; index < text.length; index += 1) {
    const char = text[index];
    const next = text[index + 1];
    if (inString) {
      output += char;
      if (char === '\\') { output += next ?? ''; index += 1; } else if (char === '"') inString = false;
    } else if (char === '"') {
      inString = true;
      output += char;
    } else if (char === '/' && next === '/') {
      const end = text.indexOf('\n', index);
      index = end === -1 ? text.length : end - 1;
    } else if (char === '/' && next === '*') {
      const end = text.indexOf('*/', index + 2);
      index = end === -1 ? text.length : end + 1;
    } else {
      output += char;
    }
  }
  return JSON.parse(output.replace(/,(\s*[}\]])/g, '$1'));
}

/** The value to hash: a PEM body is normalised so the scanner can match it however it was wrapped. */
export function digestInputFor(value) {
  if (!value.includes(PEM_MARKER)) return value;
  const body = PEM_BODY.exec(value);
  return body ? normalizePemBody(body[1]) : value;
}

function walkStrings(node, visit, ancestors = []) {
  if (Array.isArray(node)) {
    node.forEach((item) => walkStrings(item, visit, ancestors));
  } else if (node !== null && typeof node === 'object') {
    for (const [key, value] of Object.entries(node)) {
      if (!key.startsWith(COMMENT_KEY_PREFIX)) walkStrings(value, visit, [...ancestors, key]);
    }
  } else if (typeof node === 'string') {
    visit(node, ancestors);
  }
}

function isSecretSetting(ancestors) {
  const name = ancestors.at(-1) ?? '';
  if (ancestors.some((ancestor) => CONNECTION_STRINGS_SECTION.test(ancestor))) return true;
  return SECRET_SETTING_NAME.test(name) && !NON_SECRET_SETTING_SUFFIX.test(name);
}

/**
 * Secret-named settings and connection strings from an appsettings-style JSON file.
 * @returns {{ value: string, kind: string }[]}
 */
export function secretsFromConfig(text) {
  const found = [];
  walkStrings(parseLenientJson(text), (value, ancestors) => {
    if (value.length < MIN_CANDIDATE_LENGTH || !isSecretSetting(ancestors)) return;
    const inConnectionStrings = ancestors.some((ancestor) => CONNECTION_STRINGS_SECTION.test(ancestor));
    found.push({ value, kind: inConnectionStrings ? KIND_CONNECTION_STRING : KIND_CONFIG_SECRET });
  });
  return found;
}

/** Key-material fields from a golden-vector JSON file. */
export function secretsFromVectors(text) {
  const found = [];
  walkStrings(JSON.parse(stripByteOrderMark(text)), (value, ancestors) => {
    if (value.length >= MIN_CANDIDATE_LENGTH && VECTOR_KEY_FIELD.test(ancestors.at(-1) ?? '')) {
      found.push({ value, kind: KIND_VECTOR_KEY });
    }
  });
  return found;
}

// Base64 key material mixes digits and both letter cases; words and media types ("application/problem+json") do not.
const BASE64_CHARACTER_CLASSES = [/[0-9]/, /[A-Z]/, /[a-z]/];
const HEX_ONLY = /^[0-9a-fA-F]+$/;

function looksLikeKeyMaterial(value) {
  if (!FIXTURE_KEY_LITERAL.test(value)) return false;
  return HEX_ONLY.test(value) || BASE64_CHARACTER_CLASSES.every((characterClass) => characterClass.test(value));
}

/** Key-looking string literals from test code (fixture keys). */
export function secretsFromFixtureCode(text) {
  const found = [];
  for (const match of text.matchAll(STRING_LITERAL)) {
    const value = match[1] ?? match[2];
    if (looksLikeKeyMaterial(value)) found.push({ value, kind: KIND_TEST_FIXTURE });
  }
  return found;
}

/**
 * Secret-shaped values (quoted or unquoted) from compose files, CI workflows, scripts, SQL, launchSettings and
 * environment files, found with the secret scan's own rules, including values those rules would allow as code-like.
 */
export function secretsFromScript(text) {
  const rules = secretRules().filter((rule) => rule.valueGroup !== undefined && (!rule.appliesTo || rule.appliesTo(SCRIPT_RULE_PATH)));
  const found = [];
  for (const line of stripByteOrderMark(text).split(LINE_BREAK)) {
    for (const rule of rules) {
      for (const match of line.matchAll(rule.pattern)) {
        const value = match[rule.valueGroup] ?? '';
        if (value.length >= MIN_CANDIDATE_LENGTH && !isPlaceholder(value)) found.push({ value, kind: KIND_SCRIPT_SECRET });
      }
    }
  }
  return found;
}
