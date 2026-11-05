// permissions.json -> src/app/core/permissions/permission-keys.generated.ts (contract section 8).
//
//   { "permissions": [{ "key": "dashboard.view", "module": "Access", "description": "..." }] }
//   -> export const PermissionKeys = { DashboardView: 'dashboard.view' } as const;

import { PERMISSION_KEY_PATTERN, PERMISSION_KEY_WORD_SEPARATOR, generatedHeader } from './constants.mjs';

const PERMISSIONS_SOURCE = 'docs/generated/permissions.json';

/** The PascalCase constant name of a dotted permission key: `access.admin.view` -> `AccessAdminView`. */
export function permissionKeyName(key) {
  if (typeof key !== 'string' || !PERMISSION_KEY_PATTERN.test(key)) {
    throw new Error(`Invalid permission key ${JSON.stringify(key)}: expected ${PERMISSION_KEY_PATTERN}.`);
  }
  return key
    .split(PERMISSION_KEY_WORD_SEPARATOR)
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join('');
}

/** Validates the permissions.json document and returns its entries sorted by key (ordinal). */
export function readPermissionEntries(document) {
  if (document === null || typeof document !== 'object' || !Array.isArray(document.permissions)) {
    throw new Error(`${PERMISSIONS_SOURCE} must be an object with a "permissions" array.`);
  }
  const entries = document.permissions.map((entry, index) => {
    if (entry === null || typeof entry !== 'object' || typeof entry.key !== 'string') {
      throw new Error(`${PERMISSIONS_SOURCE}: permissions[${index}] has no string "key".`);
    }
    return { key: entry.key, name: permissionKeyName(entry.key), description: entry.description, module: entry.module };
  });
  assertUnique(entries, (entry) => entry.key, 'permission key');
  assertUnique(entries, (entry) => entry.name, 'constant name');
  return entries.sort((left, right) => (left.key < right.key ? -1 : left.key > right.key ? 1 : 0));
}

function assertUnique(entries, select, label) {
  const seen = new Set();
  for (const entry of entries) {
    const value = select(entry);
    if (seen.has(value)) throw new Error(`${PERMISSIONS_SOURCE}: duplicate ${label} "${value}".`);
    seen.add(value);
  }
}

/** A one-line JSDoc for a key, from its description and owning module, or '' when neither is present. */
function docComment(entry) {
  const parts = [entry.description, entry.module ? `(module ${entry.module})` : null]
    .filter((part) => typeof part === 'string' && part.trim() !== '')
    .map((part) => part.replaceAll('*/', '*\\/').replace(/\s+/g, ' ').trim());
  return parts.length === 0 ? '' : `  /** ${parts.join(' ')} */\n`;
}

/** The full text of permission-keys.generated.ts. */
export function renderPermissionKeys(document) {
  const members = readPermissionEntries(document)
    .map((entry) => `${docComment(entry)}  ${entry.name}: '${entry.key}',\n`)
    .join('');
  return (
    generatedHeader(PERMISSIONS_SOURCE) +
    '\n' +
    `export const PermissionKeys = {\n${members}} as const;\n` +
    '\n' +
    'export type PermissionKey = typeof PermissionKeys[keyof typeof PermissionKeys];\n'
  );
}
