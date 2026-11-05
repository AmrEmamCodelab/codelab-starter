// Batch access to git blobs: read many with one `git cat-file --batch`, write many with one
// `git hash-object -w --stdin-paths`. upstream-merge writes the renamed preimages so `git apply -3` can fall back
// to a real three-way merge on a renamed fork.
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { git } from './process.mjs';

const BATCH_HEADER_END = 0x0a;
const HEADER_SEPARATOR = ' ';
const MISSING = 'missing';
const TEMP_PREFIX = 'codelab-fork-blobs-';
const LINE_FEED = '\n';
const TRAILING_LF_BYTES = 1;

/**
 * Reads blobs by id. Ids git does not have are absent from the map.
 * @param {string} cwd
 * @param {string[]} ids
 * @returns {Map<string, Buffer>}
 */
export function readBlobs(cwd, ids) {
  const unique = [...new Set(ids)];
  const blobs = new Map();
  if (unique.length === 0) return blobs;
  const output = git(cwd, ['cat-file', '--batch'], { input: `${unique.join(LINE_FEED)}${LINE_FEED}`, binary: true }).stdout;
  let offset = 0;
  for (const id of unique) {
    const headerEnd = output.indexOf(BATCH_HEADER_END, offset);
    const header = output.subarray(offset, headerEnd).toString('utf8').split(HEADER_SEPARATOR);
    offset = headerEnd + 1;
    if (header[1] === MISSING) continue;
    const size = Number(header[2]);
    blobs.set(id, output.subarray(offset, offset + size));
    offset += size + TRAILING_LF_BYTES;
  }
  return blobs;
}

/**
 * Writes blobs into the object database without filters and returns their ids, in input order.
 * @param {string} cwd
 * @param {Buffer[]} contents
 * @returns {string[]}
 */
export function writeBlobs(cwd, contents) {
  if (contents.length === 0) return [];
  const folder = mkdtempSync(path.join(tmpdir(), TEMP_PREFIX));
  try {
    const files = contents.map((content, index) => {
      const file = path.join(folder, String(index));
      writeFileSync(file, content);
      return file;
    });
    const output = git(cwd, ['hash-object', '-w', '--no-filters', '--stdin-paths'], { input: `${files.join(LINE_FEED)}${LINE_FEED}` }).stdout;
    return output.split(LINE_FEED).filter((line) => line.length > 0);
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
}
