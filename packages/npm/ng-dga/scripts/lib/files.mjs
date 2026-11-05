// Small file helpers shared by the package scripts: deterministic JSON, sha256 and posix relative listings.
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const NODE_MODULES = 'node_modules';
const PACKAGE_MANIFEST = 'package.json';
const JSON_INDENT = 2;
const LINE_END = '\n';
const HASH_ALGORITHM = 'sha256';
const HEX = 'hex';

export function readJson(file) {
  return JSON.parse(readFileSync(file, 'utf8'));
}

/** JSON with two-space indentation and a final LF, so a rewrite of unchanged data is byte-identical. */
export function formatJson(value) {
  return JSON.stringify(value, null, JSON_INDENT) + LINE_END;
}

export function writeJson(file, value) {
  writeFileSync(file, formatJson(value));
}

export function sha256(bytes) {
  return createHash(HASH_ALGORITHM).update(bytes).digest(HEX);
}

export function toPosix(relativePath) {
  return relativePath.split(path.sep).join(path.posix.sep);
}

/** Every file below a folder as a sorted posix path relative to it. */
export function listFilesRelative(folder) {
  return readdirSync(folder, { withFileTypes: true, recursive: true })
    .filter((entry) => entry.isFile())
    .map((entry) => toPosix(path.relative(folder, path.join(entry.parentPath, entry.name))))
    .sort();
}

/**
 * The folder of an installed npm package, found the way Node does: node_modules/<name> in the start folder or the
 * nearest parent. Package "exports" are bypassed on purpose (font packages export only their CSS). Null when absent.
 */
export function findPackageFolder(startFolder, packageName) {
  let folder = path.resolve(startFolder);
  for (;;) {
    const candidate = path.join(folder, NODE_MODULES, ...packageName.split(path.posix.sep));
    if (existsSync(path.join(candidate, PACKAGE_MANIFEST))) return candidate;
    const parent = path.dirname(folder);
    if (parent === folder) return null;
    folder = parent;
  }
}

const TEXT_EXTENSIONS = new Set(['.txt', '.svg', '.json', '.md', '.scss', '.css']);
const CRLF = /\r\n/g;

/**
 * The bytes used for comparisons and hashes: text files with LF line endings (git may check them out with CRLF on
 * Windows, see .gitattributes text=auto), binaries unchanged.
 */
export function normalizedBytes(fileName, bytes) {
  if (!TEXT_EXTENSIONS.has(path.extname(fileName).toLowerCase())) return bytes;
  return Buffer.from(bytes.toString('utf8').replace(CRLF, LINE_END), 'utf8');
}
