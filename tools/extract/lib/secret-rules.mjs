// Regex rules for secrets: private keys, provider tokens, connection-string and URL passwords, secret-named settings
// (quoted and unquoted), key-shaped values under any *Key/*Secret/*Token name, bearer tokens, RSA XML / JWK private
// parts, command-line passwords, SAS signatures and a high-entropy fallback. Known source-product key material is
// caught separately by the digest denylist (secret-scanner.mjs).
import { posix } from 'node:path';

// A value that only points at where the real secret comes from (environment, template, user-secrets) is fine.
const PLACEHOLDER_VALUE = /^(?:\$\{[^}]*\}|\$\([^)]*\)|\$[A-Za-z_][\w:]*|<[^>]*>|\{\{[^}]*\}\}|%[^%]+%|__[A-Z0-9_]+__|\*+|x+|\.\.\.|changeme|placeholder|your[-_]?\w*)$/i;
const MIN_ASSIGNED_SECRET_LENGTH = 8;
// Key material: base64/base64url of at least 24 bytes, or hex of at least 128 bits.
const MIN_KEY_MATERIAL_LENGTH = 32;
const KEY_MATERIAL = /^(?:[A-Za-z0-9+/_-]{32,}={0,2}|[0-9a-fA-F]{32,})$/;
const HEX_ONLY = /^[0-9a-fA-F]+$/;
const MIXED_CHARACTER_CLASSES = [/[0-9]/, /[A-Z]/, /[a-z]/];
// Entropy fallback: a quoted base64-ish literal this long and this random is treated as a secret.
const MIN_HIGH_ENTROPY_LENGTH = 40;
const MIN_ENTROPY_BITS_PER_CHAR = 4.5;
// Hashes and integrity strings are long and random but public (package-lock integrity, git object ids).
const PUBLIC_DIGEST_PREFIX = /^sha(?:1|256|384|512)[-:]/i;
const PUBLIC_DIGEST_BEFORE = /sha(?:1|256|384|512)[-:]$/i;

function isPublicDigest(match) {
  return PUBLIC_DIGEST_PREFIX.test(match[2]) || PUBLIC_DIGEST_BEFORE.test(match.input.slice(0, match.index + 1));
}
const REDACT_VISIBLE_CHARACTERS = 4;
const REDACTION = '****';

/** True when value is empty or a placeholder rather than a literal secret. */
export function isPlaceholder(value) {
  const trimmed = value.trim();
  return trimmed.length === 0 || PLACEHOLDER_VALUE.test(trimmed);
}

// appsettings files document a setting with a sibling "//<Name>" key whose value is prose, not a secret.
const DOCUMENTATION_KEY_PREFIX = '//';

function isDocumentationKey(match) {
  return match.input.slice(0, match.index).endsWith(DOCUMENTATION_KEY_PREFIX);
}

/** Keeps the first characters of a secret so a reader can find it, and hides the rest. */
export function redact(secret) {
  return `${secret.slice(0, REDACT_VISIBLE_CHARACTERS)}${REDACTION}`;
}

/** Shannon entropy in bits per character. */
export function entropyBitsPerChar(value) {
  const counts = new Map();
  for (const char of value) counts.set(char, (counts.get(char) ?? 0) + 1);
  let bits = 0;
  for (const count of counts.values()) {
    const probability = count / value.length;
    bits -= probability * Math.log2(probability);
  }
  return bits;
}

/** True for base64/hex key material (hex of 128+ bits, or base64 mixing digits and both letter cases). */
export function looksLikeKeyMaterial(value) {
  if (value.length < MIN_KEY_MATERIAL_LENGTH || !KEY_MATERIAL.test(value)) return false;
  return HEX_ONLY.test(value) || MIXED_CHARACTER_CLASSES.every((characterClass) => characterClass.test(value));
}

// "config-secret", "api_key.header": a lowercase identifier that names the setting itself carries no entropy.
// Only values made of the setting's own words are exempt; "correcthorsebatterystaple" is a passphrase.
const LOWERCASE_IDENTIFIER = /^[a-z]+(?:[-_.][a-z]+)*$/;
const SECRET_WORDS = /secret|password|passwd|pwd|key|token|credential/;

function namesTheSetting(value) {
  return LOWERCASE_IDENTIFIER.test(value) && SECRET_WORDS.test(value);
}

// Header and claim names (X-XSRF-TOKEN, X-Request-Id) are names, not values.
const HEADER_NAME = /^X-[A-Za-z0-9-]+$/i;

function isNameNotSecret(value) {
  return namesTheSetting(value) || HEADER_NAME.test(value);
}

// Files where an unquoted NAME=value / name: value line is configuration (env, YAML, scripts), not code.
const UNQUOTED_CONFIG_EXTENSIONS = new Set(['.env', '.yml', '.yaml', '.ps1', '.psm1', '.psd1', '.sh', '.bash', '.cmd', '.bat', '.ini', '.properties', '.conf', '.cfg', '.toml']);
const ENV_FILE_NAME = /^\.env(?:\..+)?$/i;

function isUnquotedConfigFile(path) {
  const name = posix.basename(path);
  return ENV_FILE_NAME.test(name) || UNQUOTED_CONFIG_EXTENSIONS.has(posix.extname(name).toLowerCase());
}

// Values that are code, not literals: member access, calls, types, booleans, numbers-only.
const CODE_LIKE_VALUE = /^(?:[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)+|.*\(.*|true|false|null|none|\d+)$/i;
const COMMAND_LINE_PASSWORD_TOOLS = /\b(?:sqlcmd|bcp|osql|mysql|psql|mongo\w*)\b/i;

/**
 * @typedef {{ id: string, pattern: RegExp, message: string, valueGroup?: number,
 *             appliesTo?: (path: string) => boolean,
 *             isAllowedMatch?: (match: RegExpMatchArray) => boolean }} SecretRule
 * valueGroup: the capture group holding the secret (redacted in the report); default the whole match.
 */

/** @returns {SecretRule[]} */
export function secretRules() {
  const valueIsPlaceholder = (group) => (match) => isPlaceholder(match[group] ?? '');
  const ENUM_MEMBER_VALUE = /^\d{1,3},?$/;
  return [
    {
      id: 'private-key',
      pattern: /-----BEGIN (?:[A-Z0-9]+ )*PRIVATE KEY(?: BLOCK)?-----/g,
      message: 'private key block',
    },
    { id: 'putty-private-key', pattern: /PuTTY-User-Key-File-\d/g, message: 'PuTTY private key file' },
    { id: 'aws-access-key', pattern: /\b(?:AKIA|ASIA)[0-9A-Z]{16}\b/g, message: 'AWS access key id' },
    { id: 'github-token', pattern: /\bgh[pousr]_[A-Za-z0-9]{36,}\b/g, message: 'GitHub token' },
    { id: 'npm-token', pattern: /\bnpm_[A-Za-z0-9]{36}\b/g, message: 'npm access token' },
    { id: 'slack-token', pattern: /\bxox[abprs]-[A-Za-z0-9-]{10,}/g, message: 'Slack token' },
    { id: 'google-api-key', pattern: /\bAIza[0-9A-Za-z_-]{35}\b/g, message: 'Google API key' },
    {
      id: 'jwt',
      pattern: /\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}/g,
      message: 'JSON Web Token',
    },
    { id: 'azure-storage-key', pattern: /AccountKey=[A-Za-z0-9+/=]{40,}/g, message: 'Azure storage account key' },
    {
      id: 'azure-sas-signature',
      pattern: /[?&]sig=([A-Za-z0-9%+/=]{20,})/g,
      message: 'Azure SAS signature in a URL',
      valueGroup: 1,
    },
    {
      id: 'connection-string-password',
      pattern: /\b(?:Password|Pwd)\s*=\s*([^;"'\s]+)/gi,
      message: 'password inside a connection string (supply it from the environment)',
      valueGroup: 1,
      // An enum member declaration (a password-named member set to a small integer) stores no secret.
      isAllowedMatch: (match) => valueIsPlaceholder(1)(match) || ENUM_MEMBER_VALUE.test(match[1] ?? ''),
    },
    {
      id: 'url-credentials',
      pattern: /\b[a-z][a-z0-9+.-]*:\/\/[^\s:@/"']+:([^\s@/"']+)@/gi,
      message: 'password embedded in a URL (user:password@host)',
      valueGroup: 1,
      isAllowedMatch: valueIsPlaceholder(1),
    },
    {
      id: 'secret-assignment',
      // "SecretKey": "...", api_key = '...', ClientSecret: "...", $SaPwd = '...' (JSON, YAML, env, C#/TS/PS literals).
      pattern: /\b[A-Za-z0-9_.-]*(?:secret|password|passwd|pwd|apikey|api_key|privatekey|private_key|signingkey|secretkey|clientsecret|accesskey|hashkey|ticketkey|materialbase64)["']?\s*[:=]\s*["']([^"'\r\n]*)["']/gi,
      message: 'secret-named setting with a literal value (leave it empty; supply it from the environment)',
      valueGroup: 1,
      isAllowedMatch: (match) => isDocumentationKey(match)
        || isPlaceholder(match[1])
        || match[1].length < MIN_ASSIGNED_SECRET_LENGTH
        || isNameNotSecret(match[1]),
    },
    {
      id: 'unquoted-secret-assignment',
      // .env DB_PASSWORD=..., YAML MSSQL_SA_PASSWORD: ..., PowerShell $Pwd = ... without quotes.
      pattern: /(?<![\w$])\$?[A-Za-z0-9_]*(?:password|passwd|pwd|secret|token|hashkey|ticketkey|apikey|api_key)[A-Za-z0-9_]*\s*[:=]\s*([^\s"'`$<{%#,;()[\]]{8,})(?=\s|$|[,;#])/gi,
      message: 'secret-named setting with an unquoted literal value (supply it from the environment)',
      valueGroup: 1,
      appliesTo: isUnquotedConfigFile,
      isAllowedMatch: (match) => isPlaceholder(match[1]) || CODE_LIKE_VALUE.test(match[1]) || isNameNotSecret(match[1]),
    },
    {
      id: 'key-material-setting',
      // Any setting whose name ends in Key/Keys/Secret/Token/Hmac (including a bare "Key") holding key material.
      pattern: /["']?\b([A-Za-z0-9_.-]*(?:key|keys|secret|token|hmac|keybase64|keyhex))["']?\s*[:=]\s*["']?([A-Za-z0-9+/_=-]{32,})/gi,
      message: 'key material under a key/secret/token setting (generate it per environment; never commit it)',
      valueGroup: 2,
      isAllowedMatch: (match) => !looksLikeKeyMaterial(match[2]),
    },
    {
      id: 'bearer-token',
      pattern: /\bBearer\s+([A-Za-z0-9\-._~+/]{20,}=*)/g,
      message: 'bearer token',
      valueGroup: 1,
    },
    {
      id: 'rsa-xml-private-part',
      pattern: /<(D|P|Q|DP|DQ|InverseQ)>([A-Za-z0-9+/=]{16,})<\/\1>/g,
      message: 'RSA private key part (XML key format)',
      valueGroup: 2,
    },
    {
      id: 'jwk-private-part',
      pattern: /"(?:d|p|q|dp|dq|qi|k)"\s*:\s*"([A-Za-z0-9_-]{16,})"/g,
      message: 'private or symmetric JSON Web Key part',
      valueGroup: 1,
    },
    {
      id: 'command-line-password',
      pattern: /(?:^|\s)-P\s+(["']?)([^\s"']{4,})\1/g,
      message: 'literal password on a database tool command line (the sqlcmd/bcp password switch)',
      valueGroup: 2,
      isAllowedMatch: (match) => !COMMAND_LINE_PASSWORD_TOOLS.test(match.input) || isPlaceholder(match[2]),
    },
    {
      id: 'plaintext-secure-string',
      pattern: /ConvertTo-SecureString\s+(?:-String\s+)?(["'])([^"'$]+)\1/gi,
      message: 'literal password turned into a SecureString (read it from a prompt or the environment)',
      valueGroup: 2,
      isAllowedMatch: valueIsPlaceholder(2),
    },
    {
      id: 'high-entropy-literal',
      pattern: /(["'`])([A-Za-z0-9+/_-]{40,}={0,2})\1/g,
      message: 'high-entropy literal that looks like key material',
      valueGroup: 2,
      isAllowedMatch: (match) => HEX_ONLY.test(match[2])
        || isPublicDigest(match)
        || !MIXED_CHARACTER_CLASSES.every((characterClass) => characterClass.test(match[2]))
        || match[2].length < MIN_HIGH_ENTROPY_LENGTH
        || entropyBitsPerChar(match[2]) < MIN_ENTROPY_BITS_PER_CHAR,
    },
  ];
}
