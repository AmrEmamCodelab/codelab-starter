// Applies a renameMap to a working tree: file contents (text files only) and file paths. This is the rename part of
// init (ADR-003); in -Minimal the map is the identity and nothing changes. upstream-merge replays the same renamer
// on incoming diffs, so the two always agree.
import { existsSync, mkdirSync, readdirSync, readFileSync, renameSync, rmdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { MANIFEST_FILE } from './constants.mjs';
import { isBinary } from './hashing.mjs';

const UTF8 = 'utf8';
const POSIX_SEPARATOR = '/';
const CURRENT_DIRECTORY = '.';

/** Folders a tree walk never enters at any depth: installed packages, front-end build outputs and git metadata. */
export const RENAME_SKIPPED_DIRS = Object.freeze(['node_modules', '.git', 'dist', '.angular']);
/** Folders a tree walk skips at the root only (the local package feed and build artifacts; a module may own a Feed folder). */
export const RENAME_SKIPPED_ROOT_DIRS = Object.freeze(['feed', 'artifacts']);
/** .NET build outputs; skipped only next to a project file, so a tracked tool folder named bin is still renamed. */
export const BUILD_OUTPUT_DIRS = Object.freeze(['bin', 'obj']);
export const PROJECT_FILE_PATTERN = /\.(?:cs|fs|vb|es)proj$/i;

function removeEmptyParents(root, relativeDirectory) {
  let directory = relativeDirectory;
  while (directory && directory !== CURRENT_DIRECTORY) {
    const full = path.join(root, directory);
    if (readdirSync(full).length > 0) return;
    rmdirSync(full);
    directory = path.dirname(directory);
  }
}

function renameContent(root, file, renamer) {
  const full = path.join(root, file);
  const buffer = readFileSync(full);
  if (isBinary(buffer)) return false;
  const text = buffer.toString(UTF8);
  const renamed = renamer.text(text);
  if (renamed === text) return false;
  writeFileSync(full, renamed, UTF8);
  return true;
}

/**
 * Renames contents and paths of the given repo-relative files. The manifest is never rewritten (its renameMap
 * would rename itself); callers rename its path fields explicitly.
 * @param {string} root
 * @param {string[]} files repo-relative, '/' separators
 * @param {{ identity: boolean, text: (v: string) => string, path: (v: string) => string }} renamer
 * @returns {{ contents: string[], moves: { from: string, to: string }[] }}
 */
export function renameTree(root, files, renamer) {
  const result = { contents: [], moves: [] };
  if (renamer.identity) return result;
  for (const file of files.filter((candidate) => candidate !== MANIFEST_FILE)) {
    if (renameContent(root, file, renamer)) result.contents.push(file);
    const target = renamer.path(file);
    if (target === file) continue;
    mkdirSync(path.dirname(path.join(root, target)), { recursive: true });
    renameSync(path.join(root, file), path.join(root, target));
    removeEmptyParents(root, path.dirname(file));
    result.moves.push({ from: file, to: target });
  }
  return result;
}

function isSkippedDirectory(name, parent, siblings, skippedDirs) {
  if (skippedDirs.includes(name)) return true;
  if (parent === CURRENT_DIRECTORY && RENAME_SKIPPED_ROOT_DIRS.includes(name)) return true;
  return BUILD_OUTPUT_DIRS.includes(name) && siblings.some((sibling) => PROJECT_FILE_PATTERN.test(sibling));
}

/**
 * Every file under root, repo-relative with '/' separators, sorted. Never enters RENAME_SKIPPED_DIRS, the root's
 * RENAME_SKIPPED_ROOT_DIRS, nor bin/obj beside a project file. Use it where there is no git index (a copied tree);
 * init passes git's own list instead.
 * @param {string} root
 * @param {readonly string[]} [skippedDirs]
 * @returns {string[]}
 */
export function listTreeFiles(root, skippedDirs = RENAME_SKIPPED_DIRS) {
  const files = [];
  const pending = [CURRENT_DIRECTORY];
  while (pending.length > 0) {
    const relative = pending.pop();
    const entries = readdirSync(path.join(root, relative), { withFileTypes: true });
    const names = entries.map((entry) => entry.name);
    for (const entry of entries) {
      const child = relative === CURRENT_DIRECTORY ? entry.name : `${relative}${POSIX_SEPARATOR}${entry.name}`;
      if (entry.isDirectory()) {
        if (!isSkippedDirectory(entry.name, relative, names, skippedDirs)) pending.push(child);
      } else if (entry.isFile()) {
        files.push(child);
      }
    }
  }
  return files.sort();
}

/** The folder pairs a set of moves renamed (old -> new), deepest first; only same-depth renames are paired. */
export function renamedDirectories(moves) {
  const pairs = new Map();
  for (const move of moves) {
    let from = path.posix.dirname(move.from);
    let to = path.posix.dirname(move.to);
    if (from.split(POSIX_SEPARATOR).length !== to.split(POSIX_SEPARATOR).length) continue;
    while (from !== CURRENT_DIRECTORY && from !== to) {
      pairs.set(from, to);
      from = path.posix.dirname(from);
      to = path.posix.dirname(to);
    }
  }
  const depth = (directory) => directory.split(POSIX_SEPARATOR).length;
  return [...pairs.entries()].map(([from, to]) => ({ from, to })).sort((left, right) => depth(right.from) - depth(left.from));
}

/**
 * Moves what a renamed folder still holds after its files moved (skipped build outputs, installed packages) into
 * the renamed folder, so no folder keeps the old name. An entry whose target already exists stays where it is.
 * @returns {{ from: string, to: string }[]} the carried entries
 */
export function carryLeftovers(root, moves) {
  const carried = [];
  for (const pair of renamedDirectories(moves)) {
    const source = path.join(root, pair.from);
    if (!existsSync(source)) continue;
    for (const name of readdirSync(source)) {
      const target = path.join(root, pair.to, name);
      if (existsSync(target)) continue;
      mkdirSync(path.dirname(target), { recursive: true });
      renameSync(path.join(source, name), target);
      carried.push({ from: `${pair.from}${POSIX_SEPARATOR}${name}`, to: `${pair.to}${POSIX_SEPARATOR}${name}` });
    }
    removeEmptyParents(root, pair.from);
  }
  return carried;
}

/**
 * The full rename of a working tree (init Step 1): contents and paths of every listed file (default: the tree walk),
 * then the leftovers of renamed folders. Folders are renamed through their files' paths, so a renameMap entry that
 * spans '/' renames the same way here as in upstream-merge's replay.
 * @param {string} root
 * @param {{ identity: boolean, text: (v: string) => string, path: (v: string) => string }} renamer
 * @param {{ files?: string[] }} [options]
 * @returns {{ contents: string[], moves: { from: string, to: string }[], carried: { from: string, to: string }[] }}
 */
export function renameWorkingTree(root, renamer, options = {}) {
  if (renamer.identity) return { contents: [], moves: [], carried: [] };
  const result = renameTree(root, options.files ?? listTreeFiles(root), renamer);
  return { ...result, carried: carryLeftovers(root, result.moves) };
}
