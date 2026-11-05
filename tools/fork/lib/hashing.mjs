// Content hashes of source-owned files (codelab.starter.json `hashes`): sha256 over LF-normalized text, so a CRLF
// checkout and an LF checkout of the same file hash the same. Binary files hash as they are.
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { BINARY_SNIFF_BYTES, HASH_ALGORITHM, HASH_ENCODING, HASH_EXCLUDED_PREFIXES, NUL_BYTE } from './constants.mjs';

const CRLF = Buffer.from('\r\n');
const LF = Buffer.from('\n');

/** True when the buffer looks binary (a NUL byte in its first block, git's heuristic). */
export function isBinary(buffer) {
  return buffer.subarray(0, BINARY_SNIFF_BYTES).includes(NUL_BYTE);
}

/** Replaces every CRLF with LF (lone CRs are kept). */
export function normalizeLineEndings(buffer) {
  const parts = [];
  let start = 0;
  let index = buffer.indexOf(CRLF, start);
  while (index !== -1) {
    parts.push(buffer.subarray(start, index), LF);
    start = index + CRLF.length;
    index = buffer.indexOf(CRLF, start);
  }
  if (parts.length === 0) return buffer;
  parts.push(buffer.subarray(start));
  return Buffer.concat(parts);
}

/** The manifest hash of one file's content. */
export function hashContent(buffer) {
  const normalized = isBinary(buffer) ? buffer : normalizeLineEndings(buffer);
  return createHash(HASH_ALGORITHM).update(normalized).digest(HASH_ENCODING);
}

/** True for a path that is never hashed (the manifest itself, the feed). */
export function isHashExcluded(relativePath) {
  return HASH_EXCLUDED_PREFIXES.some((prefix) => relativePath === prefix || relativePath.startsWith(prefix));
}

/**
 * Hashes the given repo-relative files under `root`, sorted by path.
 * @param {string} root
 * @param {string[]} files repo-relative, '/' separators
 * @returns {Record<string, string>}
 */
export function computeHashes(root, files) {
  const hashes = {};
  for (const file of [...files].filter((candidate) => !isHashExcluded(candidate)).sort()) {
    hashes[file] = hashContent(readFileSync(path.join(root, file)));
  }
  return hashes;
}
