import { readdirSync, readFileSync, existsSync, mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { IGNORED_DIR_NAMES, MODULE_FILES, NEWLINE } from './constants.mjs';

/** Repo-relative path with forward slashes ('' for the root itself). */
export function toPosixRelative(root, absolutePath) {
  return path.relative(root, absolutePath).split(path.sep).join('/');
}

/** Absolute path for a repo-relative posix path. */
export function fromPosixRelative(root, relativePath) {
  return path.join(root, ...relativePath.split('/').filter(Boolean));
}

/** Joins posix path segments, skipping empty ones. */
export function joinPosix(...segments) {
  return segments.filter((segment) => segment !== '').join('/');
}

/** Text with CRLF folded to LF, so drift checks ignore checkout line endings. */
export function normalizeNewlines(text) {
  return text.replace(/\r\n/g, NEWLINE);
}

export function readText(absolutePath) {
  return readFileSync(absolutePath, 'utf8');
}

export function readTextIfExists(absolutePath) {
  return existsSync(absolutePath) ? readText(absolutePath) : undefined;
}

export function writeText(absolutePath, content) {
  mkdirSync(path.dirname(absolutePath), { recursive: true });
  writeFileSync(absolutePath, content, 'utf8');
}

/**
 * Walks `root` depth-first (sorted, deterministic), skipping IGNORED_DIR_NAMES, and calls `visit(dir, entries)`
 * for every directory.
 */
export function walkDirectories(root, visit) {
  const pending = [root];
  while (pending.length > 0) {
    const dir = pending.pop();
    const entries = readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name));
    visit(dir, entries);
    const children = entries
      .filter((entry) => entry.isDirectory() && !IGNORED_DIR_NAMES.has(entry.name))
      .map((entry) => path.join(dir, entry.name));
    pending.push(...children.reverse());
  }
}

/** Absolute paths of every module.rules.yaml under `root`, sorted by repo-relative path. */
export function findModuleRuleFiles(root) {
  const found = [];
  walkDirectories(root, (dir, entries) => {
    if (entries.some((entry) => entry.isFile() && entry.name === MODULE_FILES.rules)) {
      found.push(path.join(dir, MODULE_FILES.rules));
    }
  });
  return found.sort((a, b) => toPosixRelative(root, a).localeCompare(toPosixRelative(root, b)));
}

/** Absolute paths of every file named one of `fileNames` under `root`. */
export function findFilesNamed(root, fileNames) {
  const wanted = new Set(fileNames);
  const found = [];
  walkDirectories(root, (dir, entries) => {
    for (const entry of entries) {
      if (entry.isFile() && wanted.has(entry.name)) {
        found.push(path.join(dir, entry.name));
      }
    }
  });
  return found;
}
