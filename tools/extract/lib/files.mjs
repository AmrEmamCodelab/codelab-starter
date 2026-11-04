// File discovery and text loading for the sweeps. Every file is scanned as text: UTF-8, UTF-16 (with or without a
// BOM) and the text entries of zip containers (docx, xlsx, pptx, nupkg, zip...). A binary file that is none of these
// is "unscannable": the scrub sweep fails on it unless its extension is on UNSCANNABLE_BINARY_ALLOWLIST.
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { extname, join, relative } from 'node:path';
import { toPosixPath } from './glob.mjs';
import { readZipEntries } from './zip.mjs';

// Folders never walked outside git. Only VCS data: an extract staging folder is scanned completely, so nothing in a
// batch (a nested dist/, artifacts/ or bin/) can be promoted unscanned.
export const SKIPPED_DIRECTORIES = new Set(['.git']);
// git's own binary heuristic: a NUL byte in the first 8000 bytes.
const BINARY_SNIFF_BYTES = 8000;
const NUL_BYTE = 0;
const GIT_LIST_ARGS = ['ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', '.'];
const GIT_MAX_BUFFER_BYTES = 256 * 1024 * 1024;
const QUIET_STDIO = ['ignore', 'pipe', 'ignore'];

const UTF8_BOM = Buffer.from([0xef, 0xbb, 0xbf]);
const UTF16LE_BOM = Buffer.from([0xff, 0xfe]);
const UTF16BE_BOM = Buffer.from([0xfe, 0xff]);
// Without a BOM, UTF-16 text of mostly ASCII has a NUL in (nearly) every other byte.
const UTF16_NUL_RATIO = 0.9;
const MIN_UTF16_SNIFF_BYTES = 4;
const UTF16_UNIT_BYTES = 2;

/** Zip-based containers whose entries are scanned. */
const ZIP_CONTAINER_EXTENSIONS = new Set([
  '.zip', '.docx', '.xlsx', '.pptx', '.docm', '.xlsm', '.odt', '.ods', '.odp', '.nupkg', '.snupkg', '.vsix', '.jar',
]);
const ZIP_SIGNATURE = Buffer.from([0x50, 0x4b, 0x03, 0x04]);
/** Binary files that may exist without being scanned (images and fonts carry no text worth scanning). */
export const UNSCANNABLE_BINARY_ALLOWLIST = new Set([
  '.png', '.jpg', '.jpeg', '.gif', '.ico', '.webp', '.woff', '.woff2', '.ttf', '.otf', '.eot',
]);
/** Separates a container path from an entry path in finding paths: "notes.docx!word/document.xml". */
export const ENTRY_SEPARATOR = '!';

// OOXML/ODF text is split into runs (<w:t>MO</w:t><w:t>DHS</w:t>): tags are removed so words join up again.
const XML_ENTRY = /\.(?:xml|rels|vml)$/i;
const XML_BLOCK_END = /<\/(?:[\w-]+:)?(?:p|si|row|tr|br|tab|h)>|<(?:[\w-]+:)?(?:br|tab|cr)\s*\/>/gi;
const XML_TAG = /<[^>]*>/g;
const XML_ENTITIES = Object.freeze({ '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&apos;': "'" });
const XML_ENTITY = /&(?:amp|lt|gt|quot|apos);|&#(\d+);|&#x([0-9a-f]+);/gi;
const HEX_RADIX = 16;
const DECIMAL_RADIX = 10;

/**
 * Lists files under root. Inside a git work tree: tracked plus untracked-not-ignored files, so .gitignore is
 * honoured (node_modules, bin, the feed's packages). Elsewhere (an extract staging folder): every file except .git.
 * @param {string} root directory to scan
 * @returns {string[]} forward-slash paths relative to root, sorted
 */
export function listFiles(root) {
  const files = tryListGitFiles(root) ?? walk(root, root);
  return files.filter((path) => isRegularFile(join(root, path))).sort();
}

function tryListGitFiles(root) {
  try {
    const inside = execFileSync('git', ['rev-parse', '--is-inside-work-tree'], { cwd: root, stdio: QUIET_STDIO });
    if (inside.toString().trim() !== 'true') return null;
    const output = execFileSync('git', GIT_LIST_ARGS, { cwd: root, maxBuffer: GIT_MAX_BUFFER_BYTES, stdio: QUIET_STDIO });
    return output.toString('utf8').split('\0').filter(Boolean).map(toPosixPath);
  } catch {
    // Not a git work tree, or git is not installed: the caller walks the folder instead.
    return null;
  }
}

/**
 * Lists EVERY file under root, skipping nothing (for the harvest manifest check, where node_modules or bin folders
 * that the source commit tracks must be verified too).
 * @returns {string[]} forward-slash paths relative to root, sorted
 */
export function listAllFiles(root) {
  return walk(root, root, new Set()).sort();
}

function walk(root, directory, skipped = SKIPPED_DIRECTORIES) {
  const results = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const full = join(directory, entry.name);
    if (entry.isDirectory()) {
      if (!skipped.has(entry.name)) results.push(...walk(root, full, skipped));
    } else if (entry.isFile()) {
      results.push(toPosixPath(relative(root, full)));
    }
  }
  return results;
}

function isRegularFile(path) {
  try {
    return statSync(path).isFile();
  } catch {
    // Listed by git but deleted from the work tree: nothing to scan.
    return false;
  }
}

function startsWith(bytes, prefix) {
  return bytes.length >= prefix.length && bytes.subarray(0, prefix.length).equals(prefix);
}

function nulRatio(sample, offset) {
  let units = 0;
  let nuls = 0;
  for (let index = offset; index < sample.length; index += UTF16_UNIT_BYTES) {
    units += 1;
    if (sample[index] === NUL_BYTE) nuls += 1;
  }
  return units === 0 ? 0 : nuls / units;
}

function evenLength(bytes) {
  return bytes.subarray(0, bytes.length - (bytes.length % UTF16_UNIT_BYTES));
}

function decodeUtf16BigEndian(bytes) {
  const swapped = Buffer.from(evenLength(bytes));
  swapped.swap16();
  return swapped.toString('utf16le');
}

/**
 * Decodes bytes as text: UTF-8 (BOM stripped), UTF-16 LE/BE with a BOM, or UTF-16 without a BOM recognised by its
 * alternating NUL bytes. Returns null for binary content.
 * @param {Buffer} bytes
 */
export function decodeText(bytes) {
  if (startsWith(bytes, UTF8_BOM)) return bytes.subarray(UTF8_BOM.length).toString('utf8');
  if (startsWith(bytes, UTF16LE_BOM)) return evenLength(bytes.subarray(UTF16LE_BOM.length)).toString('utf16le');
  if (startsWith(bytes, UTF16BE_BOM)) return decodeUtf16BigEndian(bytes.subarray(UTF16BE_BOM.length));
  const sample = bytes.subarray(0, BINARY_SNIFF_BYTES);
  if (!sample.includes(NUL_BYTE)) return bytes.toString('utf8');
  if (sample.length >= MIN_UTF16_SNIFF_BYTES) {
    if (nulRatio(sample, 1) >= UTF16_NUL_RATIO && nulRatio(sample, 0) === 0) return evenLength(bytes).toString('utf16le');
    if (nulRatio(sample, 0) >= UTF16_NUL_RATIO && nulRatio(sample, 1) === 0) return decodeUtf16BigEndian(bytes);
  }
  return null;
}

/**
 * Reads a file as text (see decodeText), or returns null for a binary file.
 * @param {string} path absolute path
 */
export function readTextFile(path) {
  return decodeText(readFileSync(path));
}

function decodeXmlEntity(entity, decimal, hex) {
  if (decimal) return String.fromCodePoint(Number.parseInt(decimal, DECIMAL_RADIX));
  if (hex) return String.fromCodePoint(Number.parseInt(hex, HEX_RADIX));
  return XML_ENTITIES[entity.toLowerCase()];
}

/** The readable text of an XML document: tags removed (block ends become line breaks), entities decoded. */
export function xmlToText(xml) {
  return xml.replace(XML_BLOCK_END, '\n').replace(XML_TAG, '').replace(XML_ENTITY, decodeXmlEntity);
}

function isZipContainer(path, bytes) {
  return ZIP_CONTAINER_EXTENSIONS.has(extname(path).toLowerCase()) || startsWith(bytes, ZIP_SIGNATURE);
}

function binaryItem(path) {
  const allowed = UNSCANNABLE_BINARY_ALLOWLIST.has(extname(path).toLowerCase());
  return { path, text: null, ...(allowed ? {} : { unscannable: 'binary file that cannot be scanned' }) };
}

function containerTexts(containerPath, bytes) {
  const items = [];
  for (const entry of readZipEntries(bytes)) {
    const entryPath = `${containerPath}${ENTRY_SEPARATOR}${entry.name}`;
    if (entry.error) { items.push({ path: entryPath, text: null, unscannable: entry.error }); continue; }
    const text = decodeText(entry.data);
    if (text !== null) items.push({ path: entryPath, text: XML_ENTRY.test(entry.name) ? xmlToText(text) : text });
    else if (isZipContainer(entry.name, entry.data)) items.push(...containerTexts(entryPath, entry.data));
    else items.push(binaryItem(entryPath));
  }
  return items;
}

/**
 * Everything to scan for one file: [{ path, text, unscannable? }]. A text file yields itself; a zip container yields
 * one item per entry ("a.docx!word/document.xml"); any other binary yields { text: null } and, when its extension is
 * not on UNSCANNABLE_BINARY_ALLOWLIST, unscannable: <reason>.
 * @param {string} root
 * @param {string} relativePath forward-slash path relative to root
 */
export function readScannable(root, relativePath) {
  const bytes = readFileSync(join(root, relativePath));
  const text = decodeText(bytes);
  if (text !== null) return [{ path: relativePath, text }];
  if (!isZipContainer(relativePath, bytes)) return [binaryItem(relativePath)];
  try {
    return containerTexts(relativePath, bytes);
  } catch (error) {
    return [{ path: relativePath, text: null, unscannable: `unreadable container: ${error.message}` }];
  }
}
