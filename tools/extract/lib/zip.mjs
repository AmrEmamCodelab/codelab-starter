// Minimal zip reader for the sweeps (no dependencies): lists the entries of a zip container and inflates them, so
// the text inside docx/xlsx/nupkg/zip files is scanned like any other file. Stored (0) and deflate (8) entries are
// read; anything else, encrypted entries and oversized entries are reported as unscannable instead of skipped.
import { inflateRawSync } from 'node:zlib';

const END_OF_CENTRAL_DIRECTORY = 0x06054b50;
const CENTRAL_DIRECTORY_ENTRY = 0x02014b50;
const LOCAL_FILE_HEADER = 0x04034b50;
const END_RECORD_MIN_BYTES = 22;
const MAX_COMMENT_BYTES = 0xffff;
const CENTRAL_ENTRY_FIXED_BYTES = 46;
const LOCAL_HEADER_FIXED_BYTES = 30;
const METHOD_STORED = 0;
const METHOD_DEFLATE = 8;
const FLAG_ENCRYPTED = 0x1;
const DIRECTORY_SUFFIX = '/';
/** Zip-bomb guard: an entry that inflates beyond this is not read. */
export const MAX_ENTRY_BYTES = 64 * 1024 * 1024;

// Offsets inside the fixed-size records (APPNOTE.TXT section 4.3).
const EOCD = Object.freeze({ entryCount: 10, directoryOffset: 16 });
const CENTRAL = Object.freeze({
  flags: 8, method: 10, compressedSize: 20, size: 24, nameLength: 28, extraLength: 30, commentLength: 32, localOffset: 42,
});
const LOCAL = Object.freeze({ nameLength: 26, extraLength: 28 });

function findEndOfCentralDirectory(bytes) {
  const lowest = Math.max(0, bytes.length - END_RECORD_MIN_BYTES - MAX_COMMENT_BYTES);
  for (let offset = bytes.length - END_RECORD_MIN_BYTES; offset >= lowest; offset -= 1) {
    if (bytes.readUInt32LE(offset) === END_OF_CENTRAL_DIRECTORY) return offset;
  }
  throw new Error('not a zip file (no end-of-central-directory record)');
}

function readCentralEntry(bytes, offset) {
  if (bytes.readUInt32LE(offset) !== CENTRAL_DIRECTORY_ENTRY) throw new Error('corrupt central directory');
  const nameLength = bytes.readUInt16LE(offset + CENTRAL.nameLength);
  const entry = {
    flags: bytes.readUInt16LE(offset + CENTRAL.flags),
    method: bytes.readUInt16LE(offset + CENTRAL.method),
    compressedSize: bytes.readUInt32LE(offset + CENTRAL.compressedSize),
    size: bytes.readUInt32LE(offset + CENTRAL.size),
    localOffset: bytes.readUInt32LE(offset + CENTRAL.localOffset),
    name: bytes.subarray(offset + CENTRAL_ENTRY_FIXED_BYTES, offset + CENTRAL_ENTRY_FIXED_BYTES + nameLength).toString('utf8'),
  };
  const next = offset + CENTRAL_ENTRY_FIXED_BYTES + nameLength
    + bytes.readUInt16LE(offset + CENTRAL.extraLength) + bytes.readUInt16LE(offset + CENTRAL.commentLength);
  return { entry, next };
}

function entryData(bytes, entry) {
  if (bytes.readUInt32LE(entry.localOffset) !== LOCAL_FILE_HEADER) throw new Error('corrupt local header');
  const start = entry.localOffset + LOCAL_HEADER_FIXED_BYTES
    + bytes.readUInt16LE(entry.localOffset + LOCAL.nameLength) + bytes.readUInt16LE(entry.localOffset + LOCAL.extraLength);
  const raw = bytes.subarray(start, start + entry.compressedSize);
  return entry.method === METHOD_STORED ? raw : inflateRawSync(raw, { maxOutputLength: MAX_ENTRY_BYTES });
}

function entryProblem(entry) {
  if (entry.flags & FLAG_ENCRYPTED) return 'encrypted zip entry';
  if (entry.method !== METHOD_STORED && entry.method !== METHOD_DEFLATE) return `unsupported zip method ${entry.method}`;
  if (entry.size > MAX_ENTRY_BYTES) return `zip entry larger than ${MAX_ENTRY_BYTES} bytes`;
  return null;
}

/**
 * The file entries of a zip archive: [{ name, data }] or [{ name, error }] for an entry that cannot be read.
 * @param {Buffer} bytes the whole archive
 */
export function readZipEntries(bytes) {
  const end = findEndOfCentralDirectory(bytes);
  const count = bytes.readUInt16LE(end + EOCD.entryCount);
  let offset = bytes.readUInt32LE(end + EOCD.directoryOffset);
  const entries = [];
  for (let index = 0; index < count; index += 1) {
    const { entry, next } = readCentralEntry(bytes, offset);
    offset = next;
    if (entry.name.endsWith(DIRECTORY_SUFFIX)) continue;
    const problem = entryProblem(entry);
    if (problem) { entries.push({ name: entry.name, error: problem }); continue; }
    try {
      entries.push({ name: entry.name, data: entryData(bytes, entry) });
    } catch (error) {
      entries.push({ name: entry.name, error: `unreadable zip entry: ${error.message}` });
    }
  }
  return entries;
}
