// Content hashes used for origin entries and the secret digest denylist.
import { createHash } from 'node:crypto';

const GIT_BLOB_HEADER = 'blob';

/** SHA-256 hex of a string (UTF-8) or bytes. */
export function sha256Hex(data) {
  return createHash('sha256').update(data).digest('hex');
}

/**
 * The git blob id of raw bytes: SHA-1 over "blob <length>\0<bytes>". Equal to `git hash-object --no-filters`.
 * @param {Buffer} bytes
 */
export function gitBlobId(bytes) {
  return createHash('sha1').update(`${GIT_BLOB_HEADER} ${bytes.length}\0`).update(bytes).digest('hex');
}

const DIGEST_RECORD_SEPARATOR = '\0';
const DIGEST_RECORD_END = '\n';

/**
 * Pins a folder's content: SHA-256 over "<path>\0<sha256 of file>\n" for every file, in path order. Used for batch
 * sources that are not a git commit (manifest batches.<name>.source.contentDigest).
 * @param {{ path: string, bytes: Buffer }[]} files forward-slash relative paths
 */
export function contentDigest(files) {
  const hash = createHash('sha256');
  for (const { path, bytes } of [...files].sort((left, right) => (left.path < right.path ? -1 : 1))) {
    hash.update(`${path}${DIGEST_RECORD_SEPARATOR}${sha256Hex(bytes)}${DIGEST_RECORD_END}`);
  }
  return hash.digest('hex');
}
