// Reads, validates and writes codelab.starter.json. The validation mirrors tools/fork/codelab.starter.schema.json
// (the tests run both over the same samples), so the scripts need no npm dependency in a fresh fork clone.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { JSON_INDENT, MANIFEST_FILE, MANIFEST_SCHEMA_VERSION, MODULE_NAME_PATTERN, PRODUCT_PREFIX_PATTERN, UTF8_BOM } from './constants.mjs';

/** Field order of the written manifest (the schema's `required` order). */
export const MANIFEST_FIELDS = Object.freeze([
  'schemaVersion', 'starterVersion', 'productPrefix', 'productName', 'starterBase', 'renameMap',
  'requiredModules', 'removedModules', 'prefixTargets', 'hashes',
]);

const SEMVER = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/;
const COMMIT = /^[0-9a-f]{40}([0-9a-f]{24})?$/;
const SHA256_HEX = /^[0-9a-f]{64}$/;
const PREFIX_TARGET_FILE = /^[^\\:]+\.json$/;
const JSON_PATH = /^[A-Za-z0-9_$-]+([:.][A-Za-z0-9_$-]+)*$/;
const BACKSLASH = '\\';
const LANGUAGES = Object.freeze(['en', 'ar']);
const RENAME_FIELDS = Object.freeze(['from', 'to']);
const PREFIX_TARGET_FIELDS = Object.freeze(['file', 'jsonPath']);

/** Raised for a missing, unreadable or invalid manifest. */
export class ManifestError extends Error {}

const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);
const isNonEmptyString = (value) => typeof value === 'string' && value.length > 0;

function exactKeys(value, keys) {
  return isObject(value) && Object.keys(value).length === keys.length && keys.every((key) => key in value);
}

function checkModules(errors, field, value) {
  if (!Array.isArray(value)) return errors.push(`${field} must be an array.`);
  if (new Set(value).size !== value.length) errors.push(`${field} has duplicates.`);
  for (const name of value) {
    if (typeof name !== 'string' || !MODULE_NAME_PATTERN.test(name)) errors.push(`${field}: '${name}' is not a module name.`);
  }
  return errors.length;
}

/** Every schema violation of a renameMap (empty when valid). */
export function renameMapErrors(value) {
  if (!Array.isArray(value)) return ['renameMap must be an array.'];
  return value
    .filter((entry) => !exactKeys(entry, RENAME_FIELDS) || !isNonEmptyString(entry.from) || !isNonEmptyString(entry.to))
    .map((entry) => `renameMap entries are { from, to } with non-empty strings: ${JSON.stringify(entry)}`);
}

function checkRenameMap(errors, value) {
  errors.push(...renameMapErrors(value));
}

function checkPrefixTargets(errors, value) {
  if (!Array.isArray(value)) return errors.push('prefixTargets must be an array.');
  for (const entry of value) {
    const valid = exactKeys(entry, PREFIX_TARGET_FIELDS)
      && typeof entry.file === 'string' && PREFIX_TARGET_FILE.test(entry.file)
      && typeof entry.jsonPath === 'string' && JSON_PATH.test(entry.jsonPath);
    if (!valid) errors.push(`prefixTargets entries are { file: <path>.json, jsonPath: a:b.c }: ${JSON.stringify(entry)}`);
  }
  return errors.length;
}

function checkHashes(errors, value) {
  if (!isObject(value)) return errors.push('hashes must be an object.');
  for (const [file, hash] of Object.entries(value)) {
    if (file.includes(BACKSLASH) || typeof hash !== 'string' || !SHA256_HEX.test(hash)) {
      errors.push(`hashes['${file}'] must be a '/'-separated path mapped to a sha256 hex digest.`);
    }
  }
  return errors.length;
}

function checkScalars(errors, manifest) {
  if (manifest.schemaVersion !== MANIFEST_SCHEMA_VERSION) errors.push(`schemaVersion must be ${MANIFEST_SCHEMA_VERSION}.`);
  if (typeof manifest.starterVersion !== 'string' || !SEMVER.test(manifest.starterVersion)) errors.push('starterVersion must be semver.');
  if (typeof manifest.productPrefix !== 'string' || !PRODUCT_PREFIX_PATTERN.test(manifest.productPrefix)) {
    errors.push('productPrefix must be 2-16 characters: A-Z, then A-Z or 0-9.');
  }
  if (!exactKeys(manifest.productName, LANGUAGES) || !LANGUAGES.every((key) => isNonEmptyString(manifest.productName[key]))) {
    errors.push('productName must be { en, ar } with non-empty strings.');
  }
  if (manifest.starterBase !== null && (typeof manifest.starterBase !== 'string' || !COMMIT.test(manifest.starterBase))) {
    errors.push('starterBase must be null or a full commit id.');
  }
}

/** Every schema violation of a parsed manifest (empty when valid). */
export function validateManifest(manifest) {
  if (!isObject(manifest)) return ['The manifest must be a JSON object.'];
  const errors = [];
  const missing = MANIFEST_FIELDS.filter((field) => !(field in manifest));
  const unknown = Object.keys(manifest).filter((field) => !MANIFEST_FIELDS.includes(field));
  if (missing.length > 0) errors.push(`Missing field(s): ${missing.join(', ')}.`);
  if (unknown.length > 0) errors.push(`Unknown field(s): ${unknown.join(', ')}.`);
  if (missing.length > 0) return errors;
  checkScalars(errors, manifest);
  checkRenameMap(errors, manifest.renameMap);
  checkModules(errors, 'requiredModules', manifest.requiredModules);
  checkModules(errors, 'removedModules', manifest.removedModules);
  checkPrefixTargets(errors, manifest.prefixTargets);
  checkHashes(errors, manifest.hashes);
  return errors;
}

/** The manifest path under a repo root. */
export function manifestPath(root) {
  return path.join(root, MANIFEST_FILE);
}

/** Reads and validates the manifest; throws ManifestError when it is missing or invalid. */
export function readManifest(root) {
  const file = manifestPath(root);
  if (!existsSync(file)) throw new ManifestError(`${MANIFEST_FILE} not found at ${root}.`);
  let manifest;
  try {
    manifest = JSON.parse(readFileSync(file, 'utf8').replace(UTF8_BOM, ''));
  } catch (error) {
    throw new ManifestError(`${MANIFEST_FILE} is not valid JSON: ${error.message}`);
  }
  const errors = validateManifest(manifest);
  if (errors.length > 0) throw new ManifestError(`${MANIFEST_FILE} is invalid:\n  ${errors.join('\n  ')}`);
  return manifest;
}

/** Serializes a manifest in the canonical field order with sorted hashes. */
export function formatManifest(manifest) {
  const ordered = {};
  for (const field of MANIFEST_FIELDS) ordered[field] = manifest[field];
  ordered.hashes = Object.fromEntries(Object.entries(manifest.hashes).sort(([left], [right]) => (left < right ? -1 : 1)));
  return `${JSON.stringify(ordered, null, JSON_INDENT)}\n`;
}

/** Validates and writes the manifest; throws ManifestError (and writes nothing) when it is invalid. */
export function writeManifest(root, manifest) {
  const errors = validateManifest(manifest);
  if (errors.length > 0) throw new ManifestError(`Refusing to write an invalid ${MANIFEST_FILE}:\n  ${errors.join('\n  ')}`);
  writeFileSync(manifestPath(root), formatManifest(manifest));
}

/** Reads, changes (through `update`) and writes the manifest. */
export function updateManifest(root, update) {
  const draft = structuredClone(readManifest(root));
  const next = update(draft) ?? draft;
  writeManifest(root, next);
  return next;
}
