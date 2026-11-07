// The repository as the checks see it: every file (git-aware, so .gitignore is honoured and CI sees the same tree),
// every folder that holds a file, and lookups by exact path, by path suffix and by glob. Paths are case-sensitive.
import { posix } from 'node:path';
import { listFiles } from '../../extract/lib/files.mjs';
import { globToRegExp } from '../../extract/lib/glob.mjs';
import { PARENT_SEGMENT, PATH_SEPARATOR } from './constants.mjs';

const ANY_DEPTH_PREFIX = '**/';
const CURRENT_DIRECTORY = '.';

/**
 * Builds the index from a file list (repo-relative, forward slashes).
 * @param {string} root absolute repository root
 * @param {string[]} files
 */
export function createRepoIndex(root, files) {
  const fileSet = new Set(files);
  const directorySet = new Set();
  for (const file of files) {
    for (let directory = posix.dirname(file); directory !== CURRENT_DIRECTORY; directory = posix.dirname(directory)) {
      if (directorySet.has(directory)) break;
      directorySet.add(directory);
    }
  }
  const entriesByName = new Map();
  for (const entry of [...fileSet, ...directorySet]) {
    const name = posix.basename(entry);
    const list = entriesByName.get(name);
    if (list) list.push(entry);
    else entriesByName.set(name, [entry]);
  }
  return { root, files, fileSet, directorySet, entriesByName };
}

/** Lists the files under root (see tools/extract/lib/files.mjs) and indexes them. */
export function buildRepoIndex(root) {
  return createRepoIndex(root, listFiles(root));
}

/**
 * Normalizes a repo-relative path ("a/./b/../c" -> "a/c"). Returns null when it leaves the repository.
 * @param {string} path forward slashes
 */
export function normalizeRelative(path) {
  const normalized = posix.normalize(path).replace(/\/$/, '');
  if (normalized === PARENT_SEGMENT || normalized.startsWith(`${PARENT_SEGMENT}${PATH_SEPARATOR}`)) return null;
  return normalized === CURRENT_DIRECTORY ? '' : normalized;
}

/** True when the path is a file or a folder (directoryOnly: a folder) in the index. */
export function entryExists(index, path, { directoryOnly = false } = {}) {
  if (path === '') return true;
  return index.directorySet.has(path) || (!directoryOnly && index.fileSet.has(path));
}

/** True when some indexed entry ends with "/<path>" (or is <path>): how docs name a file by its tail. */
export function suffixExists(index, path, { directoryOnly = false } = {}) {
  const candidates = index.entriesByName.get(posix.basename(path)) ?? [];
  return candidates.some((entry) =>
    (entry === path || entry.endsWith(`${PATH_SEPARATOR}${path}`))
    && (!directoryOnly || index.directorySet.has(entry)));
}

/**
 * The places a relative reference may be anchored: the working folder (when known), the repository root and the
 * document's own folder, in that order. Each is normalized; anchors that leave the repository are dropped.
 */
export function anchoredCandidates(reference, { documentDirectory, workingDirectory = null }) {
  const bases = [workingDirectory, '', documentDirectory].filter((base) => base !== null && base !== undefined);
  const candidates = bases.map((base) => normalizeRelative(base === '' ? reference : `${base}${PATH_SEPARATOR}${reference}`));
  return [...new Set(candidates.filter((candidate) => candidate !== null))];
}

/**
 * Resolves a referenced path: anchored (see anchoredCandidates), else, when allowSuffix, as the tail of any entry.
 * @returns {boolean}
 */
export function referenceResolves(index, reference, { documentDirectory, workingDirectory = null, directoryOnly = false, allowSuffix = true }) {
  if (anchoredCandidates(reference, { documentDirectory, workingDirectory })
    .some((candidate) => entryExists(index, candidate, { directoryOnly }))) {
    return true;
  }
  const tail = normalizeRelative(reference);
  return allowSuffix && tail !== null && tail !== '' && suffixExists(index, tail, { directoryOnly });
}

/**
 * True when a glob matches at least one file or folder: anchored at the working folder, the root or the document's
 * folder, or (allowSuffix) at any depth.
 */
export function globMatches(index, pattern, { documentDirectory, workingDirectory = null, allowSuffix = true }) {
  const anchored = anchoredCandidates(pattern, { documentDirectory, workingDirectory });
  const expressions = anchored.map((candidate) => globToRegExp(candidate, { anchored: true }));
  if (allowSuffix) expressions.push(globToRegExp(`${ANY_DEPTH_PREFIX}${pattern.replace(/^\.\//, '')}`, { anchored: true }));
  const entries = [...index.fileSet, ...index.directorySet];
  return expressions.some((expression) => entries.some((entry) => expression.test(entry)));
}

/** The nearest folder at or above `directory` that holds the named file, or null. */
export function nearestDirectoryWith(index, directory, fileName) {
  for (let current = directory; ; current = posix.dirname(current)) {
    const normalized = current === CURRENT_DIRECTORY ? '' : current;
    const candidate = normalized === '' ? fileName : `${normalized}${PATH_SEPARATOR}${fileName}`;
    if (index.fileSet.has(candidate)) return normalized;
    if (normalized === '') return null;
  }
}
