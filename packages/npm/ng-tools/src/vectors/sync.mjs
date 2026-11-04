// Golden-vector sync: copies the starter's packages/vectors into a consumer (e.g. a fork's frontend test folder), or,
// with check, reports drift without writing. The vectors are the cross-language contract between Codelab.Security
// and @codelab/ng-security, so a consumer that runs them must hold an exact copy.

import { createHash } from 'node:crypto';
import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { dirname, join, relative, sep } from 'node:path';

/** Drift kinds a check reports. */
export const DriftKinds = Object.freeze({
  missing: 'missing',
  changed: 'changed',
  extra: 'extra',
});

const POSIX_SEPARATOR = '/';
const HASH = 'sha256';

/** Every file under `root`, as forward-slash paths relative to it, sorted. An absent folder has none. */
export function listFiles(root) {
  if (!existsSync(root)) return [];
  const files = [];
  const walk = (folder) => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name);
      if (entry.isDirectory()) walk(path);
      else if (entry.isFile()) files.push(relative(root, path).split(sep).join(POSIX_SEPARATOR));
    }
  };
  walk(root);
  return files.sort();
}

function digest(path) {
  return createHash(HASH).update(readFileSync(path)).digest('hex');
}

/**
 * What differs between `from` and `to`: files missing from `to`, files whose bytes differ, and files only in `to`.
 * @returns {{ path: string, kind: string }[]}
 */
export function compareFolders({ from, to }) {
  const source = listFiles(from);
  const target = new Set(listFiles(to));
  const drift = [];
  for (const path of source) {
    if (!target.has(path)) drift.push({ path, kind: DriftKinds.missing });
    else if (digest(join(from, path)) !== digest(join(to, path))) drift.push({ path, kind: DriftKinds.changed });
    target.delete(path);
  }
  for (const path of target) drift.push({ path, kind: DriftKinds.extra });
  return drift;
}

/**
 * Makes `to` an exact copy of `from`: copies missing and changed files and, with prune, deletes files that exist
 * only in `to`. Without prune those are left in place and reported.
 * @returns {{ copied: string[], removed: string[], extra: string[] }}
 */
export function syncFolders({ from, to, prune = false }) {
  if (!existsSync(from) || !statSync(from).isDirectory()) throw new Error(`Vector source folder not found: ${from}`);
  const result = { copied: [], removed: [], extra: [] };
  for (const { path, kind } of compareFolders({ from, to })) {
    if (kind === DriftKinds.extra) {
      if (prune) {
        rmSync(join(to, path));
        result.removed.push(path);
      } else {
        result.extra.push(path);
      }
      continue;
    }
    mkdirSync(dirname(join(to, path)), { recursive: true });
    copyFileSync(join(from, path), join(to, path));
    result.copied.push(path);
  }
  return result;
}
