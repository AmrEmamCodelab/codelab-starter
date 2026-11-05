// Parses and rewrites the output of `git diff --binary --full-index --no-renames` (one section per file).
// Pure functions: no git, no file system. upstream-merge uses them to drop paths and to rename paths and content.

const DIFF_HEADER = 'diff --git ';
const HUNK_HEADER = /^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(.*)$/;
const INDEX_LINE = /^index ([0-9a-f]+)\.\.([0-9a-f]+)(?: (\d+))?$/;
const OLD_PATH_LINE = '--- ';
const NEW_PATH_LINE = '+++ ';
const SOURCE_PREFIX = 'a/';
const DESTINATION_PREFIX = 'b/';
const DEV_NULL = '/dev/null';
const NEW_FILE_LINE = 'new file mode ';
const DELETED_FILE_LINE = 'deleted file mode ';
const BINARY_PATCH_LINE = 'GIT binary patch';
const BINARY_DIFFERS_LINE = /^Binary files .* differ$/;
const QUOTE = '"';
const ESCAPES = Object.freeze({ a: '\x07', b: '\b', t: '\t', n: '\n', v: '\v', f: '\f', r: '\r', '"': '"', '\\': '\\' });
const OCTAL_ESCAPE = /^[0-7]{3}$/;
const OCTAL_RADIX = 8;
const OCTAL_ESCAPE_LENGTH = 3;
const NEEDS_QUOTING = /["\\\t\n\r\x00-\x1f]/;
const DEFAULT_HUNK_COUNT = 1;
const SPACE = ' ';

/** Line kinds inside a hunk. */
export const HunkLine = Object.freeze({ context: ' ', added: '+', removed: '-', noNewline: '\\' });

/**
 * @typedef {{ oldStart: number, oldCount: number, newStart: number, newCount: number, section: string, lines: string[] }} Hunk
 * @typedef {{
 *   path: string, quoted: boolean, headers: string[], hunks: Hunk[], binary: string[] | null,
 *   isNew: boolean, isDeleted: boolean, oldBlob: string | null, newBlob: string | null,
 * }} FilePatch
 */

/** Decodes a git C-quoted path ("a/x\tb" with octal UTF-8 bytes). */
export function unquotePath(quoted) {
  const bytes = [];
  for (let index = 1; index < quoted.length - 1; index += 1) {
    const character = quoted[index];
    if (character !== '\\') {
      bytes.push(...Buffer.from(character, 'utf8'));
      continue;
    }
    const octal = quoted.slice(index + 1, index + 1 + OCTAL_ESCAPE_LENGTH);
    if (OCTAL_ESCAPE.test(octal)) {
      bytes.push(Number.parseInt(octal, OCTAL_RADIX));
      index += OCTAL_ESCAPE_LENGTH;
    } else {
      bytes.push(...Buffer.from(ESCAPES[quoted[index + 1]] ?? quoted[index + 1], 'utf8'));
      index += 1;
    }
  }
  return Buffer.from(bytes).toString('utf8');
}

/** Encodes a path the way git does when it needs quoting (only control characters, quotes and backslashes). */
export function quotePath(path) {
  const reverse = Object.fromEntries(Object.entries(ESCAPES).map(([key, value]) => [value, key]));
  let result = QUOTE;
  for (const character of path) {
    result += reverse[character] !== undefined ? `\\${reverse[character]}` : character;
  }
  return result + QUOTE;
}

/** The path of a `diff --git a/P b/P` line; with --no-renames both sides name the same path. */
export function parseHeaderPath(line) {
  const rest = line.slice(DIFF_HEADER.length);
  if (rest.startsWith(QUOTE)) {
    const end = rest.indexOf(`${QUOTE} `);
    const source = unquotePath(rest.slice(0, end + 1));
    return { path: source.slice(SOURCE_PREFIX.length), quoted: true };
  }
  const sideLength = (rest.length - SPACE.length) / 2;
  return { path: rest.slice(SOURCE_PREFIX.length, sideLength), quoted: false };
}

function formatSide(prefix, path, quoted) {
  return quoted || NEEDS_QUOTING.test(path) ? quotePath(`${prefix}${path}`) : `${prefix}${path}`;
}

function parseHunkHeader(line) {
  const match = HUNK_HEADER.exec(line);
  if (!match) return null;
  return {
    oldStart: Number(match[1]),
    oldCount: match[2] === undefined ? DEFAULT_HUNK_COUNT : Number(match[2]),
    newStart: Number(match[3]),
    newCount: match[4] === undefined ? DEFAULT_HUNK_COUNT : Number(match[4]),
    section: match[5],
    lines: [],
  };
}

function newFilePatch(line) {
  const { path, quoted } = parseHeaderPath(line);
  return { path, quoted, headers: [], hunks: [], binary: null, isNew: false, isDeleted: false, oldBlob: null, newBlob: null };
}

function readHeader(patch, line) {
  patch.headers.push(line);
  if (line.startsWith(NEW_FILE_LINE)) patch.isNew = true;
  if (line.startsWith(DELETED_FILE_LINE)) patch.isDeleted = true;
  const index = INDEX_LINE.exec(line);
  if (index) {
    patch.oldBlob = index[1];
    patch.newBlob = index[2];
  }
}

/**
 * Splits a git diff into file patches. The text is split on '\n' only, so a '\r' stays part of its line.
 * @param {string} text
 * @returns {FilePatch[]}
 */
export function parseDiff(text) {
  const patches = [];
  let patch = null;
  let hunk = null;
  const lines = text.split('\n');
  if (lines[lines.length - 1] === '') lines.pop();
  for (const line of lines) {
    if (line.startsWith(DIFF_HEADER)) {
      patch = newFilePatch(line);
      hunk = null;
      patches.push(patch);
    } else if (patch === null) {
      throw new Error(`Unexpected diff text before the first '${DIFF_HEADER.trim()}' line: ${line}`);
    } else if (patch.binary !== null) {
      patch.binary.push(line);
    } else if (line === BINARY_PATCH_LINE || BINARY_DIFFERS_LINE.test(line)) {
      patch.binary = [line];
    } else if (line.startsWith('@@')) {
      hunk = parseHunkHeader(line);
      if (!hunk) throw new Error(`Malformed hunk header in ${patch.path}: ${line}`);
      patch.hunks.push(hunk);
    } else if (hunk) {
      hunk.lines.push(line);
    } else {
      readHeader(patch, line);
    }
  }
  return patches;
}

function formatCount(start, count) {
  return `${start},${count}`;
}

function serializeHeaderLine(line, patch) {
  if (line.startsWith(OLD_PATH_LINE) && !line.endsWith(DEV_NULL)) {
    return `${OLD_PATH_LINE}${formatSide(SOURCE_PREFIX, patch.path, patch.quoted)}`;
  }
  if (line.startsWith(NEW_PATH_LINE) && !line.endsWith(DEV_NULL)) {
    return `${NEW_PATH_LINE}${formatSide(DESTINATION_PREFIX, patch.path, patch.quoted)}`;
  }
  if (INDEX_LINE.test(line)) {
    const mode = INDEX_LINE.exec(line)[3];
    return `index ${patch.oldBlob}..${patch.newBlob}${mode ? ` ${mode}` : ''}`;
  }
  return line;
}

/** Serializes one file patch (inverse of parseDiff for that section). */
export function serializePatch(patch) {
  const lines = [`${DIFF_HEADER}${formatSide(SOURCE_PREFIX, patch.path, patch.quoted)} ${formatSide(DESTINATION_PREFIX, patch.path, patch.quoted)}`];
  for (const header of patch.headers) lines.push(serializeHeaderLine(header, patch));
  if (patch.binary !== null) {
    lines.push(...patch.binary);
  }
  for (const hunk of patch.hunks) {
    lines.push(`@@ -${formatCount(hunk.oldStart, hunk.oldCount)} +${formatCount(hunk.newStart, hunk.newCount)} @@${hunk.section}`);
    lines.push(...hunk.lines);
  }
  return `${lines.join('\n')}\n`;
}

/** Serializes a list of file patches into one diff (empty string for none). */
export function serializeDiff(patches) {
  return patches.map(serializePatch).join('');
}

/** True for a patch that carries binary data (its content is never rewritten). */
export function isBinaryPatch(patch) {
  return patch.binary !== null;
}

function renameHunkLine(line, renameText) {
  const kind = line[0];
  if (kind === HunkLine.noNewline || line.length === 0) return line;
  return kind + renameText(line.slice(1));
}

/**
 * Returns a copy of the patch with its path and (for text patches) every hunk line and hunk section renamed.
 * Hunk counts never change: a rename never adds or removes a line.
 * @param {FilePatch} patch
 * @param {{ text: (value: string) => string, path: (value: string) => string }} renamer
 */
export function renamePatch(patch, renamer) {
  const hunks = isBinaryPatch(patch) ? patch.hunks : patch.hunks.map((hunk) => ({
    ...hunk,
    section: renamer.text(hunk.section),
    lines: hunk.lines.map((line) => renameHunkLine(line, renamer.text)),
  }));
  return { ...patch, path: renamer.path(patch.path), hunks };
}

/** Replaces the hunks of a patch with the hunks of another patch over the same file (a regenerated diff). */
export function withHunksOf(patch, regenerated) {
  return { ...patch, hunks: regenerated.hunks };
}
