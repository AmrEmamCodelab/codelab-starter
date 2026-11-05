// Applies a renameMap to a working tree: file contents (text files only) and file paths. This is the rename part of
// init (ADR-003); in -Minimal the map is the identity and nothing changes. upstream-merge replays the same renamer
// on incoming diffs, so the two always agree.
import { mkdirSync, readdirSync, readFileSync, renameSync, rmdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { MANIFEST_FILE } from './constants.mjs';
import { isBinary } from './hashing.mjs';

const UTF8 = 'utf8';

function removeEmptyParents(root, relativeDirectory) {
  let directory = relativeDirectory;
  while (directory && directory !== '.') {
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
