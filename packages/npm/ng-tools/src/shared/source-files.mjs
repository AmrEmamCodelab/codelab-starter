// Source-file helpers shared by the file-scanning tools (rtl-lint, icons-subset, i18n-check): listing the files of a
// folder, relative paths, 1-based positions and a tiny glob matcher. Node built-ins only.
import fs from 'node:fs';
import path from 'node:path';

export const TEMPLATE_EXTENSION = '.html';
export const SCRIPT_EXTENSION = '.ts';
/** TypeScript files that are not app code (tests, test helpers, declarations): never scanned. */
export const EXCLUDED_SCRIPT_SUFFIXES = Object.freeze(['.spec.ts', '.test.ts', '.testing.ts', '.d.ts']);
/** Folders that hold dependencies or build output, never sources. */
export const SKIPPED_FOLDERS = Object.freeze(['node_modules', 'dist', 'coverage', '.angular']);

const SKIPPED = new Set(SKIPPED_FOLDERS);
const LINE_BREAK = '\n';
const FIRST = 1;
const POSIX_SEPARATOR = '/';
const HALVES = 2;

/** True for an `.html` template or a `.ts` source that is not a spec, test helper or declaration file. */
export function isTemplateOrScript(name) {
  if (name.endsWith(TEMPLATE_EXTENSION)) return true;
  return name.endsWith(SCRIPT_EXTENSION) && !EXCLUDED_SCRIPT_SUFFIXES.some((suffix) => name.endsWith(suffix));
}

/** True when `dir` exists and is a folder. */
export function isFolder(dir) {
  return fs.existsSync(dir) && fs.statSync(dir).isDirectory();
}

/**
 * The files under `dir` (recursively, dependency and build folders skipped) that `accept(fileName)` keeps, sorted, as
 * absolute paths.
 */
export function listSourceFiles(dir, accept = isTemplateOrScript) {
  const files = [];
  const visit = (folder) => {
    for (const entry of fs.readdirSync(folder, { withFileTypes: true })) {
      const entryPath = path.join(folder, entry.name);
      if (entry.isDirectory()) {
        if (!SKIPPED.has(entry.name)) visit(entryPath);
      } else if (entry.isFile() && accept(entry.name)) {
        files.push(entryPath);
      }
    }
  };
  visit(dir);
  return files.sort();
}

/** `file` relative to `dir`, with forward slashes. */
export function toPosixRelative(dir, file) {
  return path.relative(dir, file).split(path.sep).join(POSIX_SEPARATOR);
}

/** Maps offsets of `text` to 1-based `{ line, column }`. */
export function positionFinder(text) {
  const lineStarts = [0];
  for (let index = text.indexOf(LINE_BREAK); index !== -1; index = text.indexOf(LINE_BREAK, index + FIRST)) lineStarts.push(index + FIRST);
  return (offset) => {
    let low = 0;
    let high = lineStarts.length - FIRST;
    while (low < high) {
      const middle = Math.ceil((low + high) / HALVES);
      if (lineStarts[middle] <= offset) low = middle;
      else high = middle - FIRST;
    }
    return { line: low + FIRST, column: offset - lineStarts[low] + FIRST };
  };
}

const GLOB_SPECIALS = /[.+^${}()|[\]\\]/g;
const DOUBLE_STAR_SLASH = '**/';
const DOUBLE_STAR = '**';
const STAR = '*';

// A glob over forward-slash relative paths as a regular expression: a double star and a slash match any folders (or
// none), a double star anything, a star anything within one segment. A glob without a slash matches the file name at
// any depth.
export function globToRegExp(glob) {
  const anchored = glob.includes(POSIX_SEPARATOR) ? glob : DOUBLE_STAR_SLASH + glob;
  let source = '';
  for (let index = 0; index < anchored.length; index++) {
    if (anchored.startsWith(DOUBLE_STAR_SLASH, index)) {
      source += '(?:.*/)?';
      index += DOUBLE_STAR_SLASH.length - FIRST;
    } else if (anchored.startsWith(DOUBLE_STAR, index)) {
      source += '.*';
      index += DOUBLE_STAR.length - FIRST;
    } else if (anchored[index] === STAR) {
      source += '[^/]*';
    } else {
      source += anchored[index].replace(GLOB_SPECIALS, '\\$&');
    }
  }
  return new RegExp(`^${source}$`);
}

/** True when the forward-slash relative path matches any of the globs. */
export function matchesAnyGlob(relativePath, globs) {
  return globs.some((glob) => globToRegExp(glob).test(relativePath));
}
