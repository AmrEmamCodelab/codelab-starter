// Loads allowlist.manifest.yaml, enforces hard-deny and resolves an extraction batch to concrete file copies.
import { readFileSync } from 'node:fs';
import { parse } from 'yaml';
import { globToRegExp, matchesGlob, toPosixPath } from './glob.mjs';

export const SUPPORTED_SCHEMA_VERSION = 1;
export const MODE_COPY = 'copy';
export const MODE_HEADINGS_ONLY = 'headings-only';
const SUPPORTED_MODES = new Set([MODE_COPY, MODE_HEADINGS_ONLY]);
const SEGMENT_SEPARATOR = '/';
const WILDCARD_CHARACTERS = /[*?{]/;

/**
 * Reads and validates the manifest.
 * @param {string} manifestPath
 */
export function loadManifest(manifestPath) {
  const manifest = parse(readFileSync(manifestPath, 'utf8'));
  validateManifest(manifest, manifestPath);
  return manifest;
}

function validateManifest(manifest, manifestPath) {
  const fail = (detail) => { throw new Error(`${manifestPath}: ${detail}`); };
  if (manifest?.schemaVersion !== SUPPORTED_SCHEMA_VERSION) fail(`schemaVersion must be ${SUPPORTED_SCHEMA_VERSION}`);
  for (const field of ['product', 'repository', 'commit', 'treeHash', 'harvestRoot']) {
    if (typeof manifest.source?.[field] !== 'string') fail(`source.${field} is required`);
  }
  if (!Array.isArray(manifest.hardDeny?.caseInsensitive) || !Array.isArray(manifest.hardDeny?.caseSensitive)) {
    fail('hardDeny.caseInsensitive and hardDeny.caseSensitive must be lists');
  }
  for (const [batchName, batch] of Object.entries(manifest.batches ?? {})) {
    for (const entry of batch.entries ?? []) {
      if (!entry.id || !entry.source || !entry.target) fail(`batch ${batchName}: every entry needs id, source, target`);
      if (!SUPPORTED_MODES.has(entry.mode)) fail(`batch ${batchName} entry ${entry.id}: unsupported mode "${entry.mode}"`);
    }
  }
}

function patternMatchesPath(pattern, path, caseInsensitive) {
  if (pattern.includes(SEGMENT_SEPARATOR)) return matchesGlob(path, pattern, { caseInsensitive });
  // A bare name pattern denies a matching folder OR file name anywhere in the path.
  const segmentPattern = globToRegExp(pattern, { caseInsensitive });
  return path.split(SEGMENT_SEPARATOR).some((segment) => segmentPattern.test(segment));
}

/**
 * Returns the first hard-deny pattern matching the path, or null.
 * @param {{ caseInsensitive: string[], caseSensitive: string[] }} hardDeny
 * @param {string} path harvest-relative path
 */
export function findDenyingPattern(hardDeny, path) {
  const posixPath = toPosixPath(path);
  const insensitive = hardDeny.caseInsensitive.find((pattern) => patternMatchesPath(pattern, posixPath, true));
  if (insensitive) return insensitive;
  return hardDeny.caseSensitive.find((pattern) => patternMatchesPath(pattern, posixPath, false)) ?? null;
}

/** The literal folder prefix of a glob ("a/b/**" -> "a/b/"), or "" when the first segment is already a wildcard. */
export function staticPrefix(sourceGlob) {
  const segments = sourceGlob.split(SEGMENT_SEPARATOR);
  const firstWildcard = segments.findIndex((segment) => WILDCARD_CHARACTERS.test(segment));
  if (firstWildcard === -1) return sourceGlob;
  const literal = segments.slice(0, firstWildcard);
  return literal.length === 0 ? '' : `${literal.join(SEGMENT_SEPARATOR)}${SEGMENT_SEPARATOR}`;
}

function targetFor(entry, sourcePath) {
  if (!WILDCARD_CHARACTERS.test(entry.source)) return entry.target;
  const relative = sourcePath.slice(staticPrefix(entry.source).length);
  const directory = entry.target.endsWith(SEGMENT_SEPARATOR) ? entry.target : `${entry.target}${SEGMENT_SEPARATOR}`;
  return `${directory}${relative}`;
}

/**
 * Resolves a batch against the harvest file list.
 * @param {object} manifest loaded manifest
 * @param {string} batchName
 * @param {string[]} harvestFiles harvest-relative forward-slash paths
 * @returns {{ copies: {entryId: string, source: string, target: string, mode: string}[],
 *             denied: {entryId: string, source: string, pattern: string}[], emptyEntries: string[] }}
 */
export function resolveBatch(manifest, batchName, harvestFiles) {
  const batch = manifest.batches?.[batchName];
  if (!batch) throw new Error(`unknown batch "${batchName}" (known: ${Object.keys(manifest.batches ?? {}).join(', ')})`);
  const copies = [];
  const denied = [];
  const emptyEntries = [];
  for (const entry of batch.entries) {
    const matched = harvestFiles.filter((path) => matchesGlob(path, entry.source, { anchored: true }));
    if (matched.length === 0) emptyEntries.push(entry.id);
    for (const source of matched) {
      const pattern = findDenyingPattern(manifest.hardDeny, source);
      if (pattern) {
        denied.push({ entryId: entry.id, source, pattern });
      } else {
        copies.push({ entryId: entry.id, source, target: targetFor(entry, source), mode: entry.mode });
      }
    }
  }
  return { copies, denied, emptyEntries };
}
