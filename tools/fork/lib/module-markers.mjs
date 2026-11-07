// A module's registrations in shared files, as remove-module removes them: lines and blocks carrying the module
// marker comments (MODULE_MARKER), and the module's `<camelName>.` keys in the translation files. Pure functions
// over text; remove-module.mjs reads and writes the files.
import { I18N_KEY_SEPARATOR, JSON_INDENT, MODULE_MARKER, UTF8_BOM } from './constants.mjs';

const REGEX_SPECIAL = /[.*+?^${}()|[\]\\]/g;
const LINE_BREAK = '\n';
const CRLF = '\r\n';
const LEADING_INDENT = /^\{\r?\n([ \t]+)"/;
const NO_KEYS = Object.freeze([]);

/** Raised for marker comments that do not pair up; the caller refuses before changing anything. */
export class MarkerError extends Error {}

/** "ServiceRequests" -> "serviceRequests" (the module's i18n key prefix). */
export function toCamelCase(name) {
  return name.length === 0 ? name : name[0].toLowerCase() + name.slice(1);
}

function escapeRegex(value) {
  return value.replace(REGEX_SPECIAL, '\\$&');
}

/** `<opener> <marker> <Name>` followed by whitespace, the HTML comment close or the end of the line. */
function markerPattern(marker, name, commentOpeners) {
  const openers = commentOpeners.map(escapeRegex).join('|');
  const close = escapeRegex(MODULE_MARKER.htmlCommentClose);
  return new RegExp(`(?:${openers})\\s*${escapeRegex(marker)}\\s+${escapeRegex(name)}(?=\\s|${close}|$)`);
}

/**
 * The three marker matchers of one module.
 * @param {string} name
 * @param {{ commentOpeners?: readonly string[] }} [options] the comment openers a marker may follow (default
 *   MODULE_MARKER.commentOpeners; removeOptionalModules adds `#` for PowerShell)
 */
export function createMarkerMatchers(name, { commentOpeners = MODULE_MARKER.commentOpeners } = {}) {
  return {
    line: markerPattern(MODULE_MARKER.line, name, commentOpeners),
    begin: markerPattern(MODULE_MARKER.begin, name, commentOpeners),
    end: markerPattern(MODULE_MARKER.end, name, commentOpeners),
  };
}

/** True when the text holds any marker of the module (a quick filter before stripping). */
export function hasModuleMarkers(text, name, options = {}) {
  const matchers = createMarkerMatchers(name, options);
  return text.split(LINE_BREAK).some((line) => matchers.line.test(line) || matchers.begin.test(line) || matchers.end.test(line));
}

function classify(line, matchers) {
  if (matchers.begin.test(line)) return MODULE_MARKER.begin;
  if (matchers.end.test(line)) return MODULE_MARKER.end;
  if (matchers.line.test(line)) return MODULE_MARKER.line;
  return null;
}

/**
 * Removes the module's marker lines and begin/end blocks (both marker lines included) from `text`. The other lines,
 * and their line endings, stay as they were.
 * @param {string} text
 * @param {string} name PascalCase module name
 * @param {string} [label] the file name used in error messages
 * @param {{ commentOpeners?: readonly string[] }} [options] see createMarkerMatchers
 * @returns {{ text: string, removedLines: number }}
 * @throws {MarkerError} for an end without a begin, a begin inside a block of the same module, or a missing end
 */
export function stripModuleMarkers(text, name, label = 'text', options = {}) {
  const matchers = createMarkerMatchers(name, options);
  const lines = text.split(LINE_BREAK);
  const kept = [];
  let blockStart = null;
  lines.forEach((line, index) => {
    const kind = classify(line, matchers);
    const lineNumber = index + 1;
    if (kind === MODULE_MARKER.begin) {
      if (blockStart !== null) throw new MarkerError(`${label}:${lineNumber}: ${MODULE_MARKER.begin} ${name} inside the block opened at line ${blockStart}.`);
      blockStart = lineNumber;
      return;
    }
    if (kind === MODULE_MARKER.end) {
      if (blockStart === null) throw new MarkerError(`${label}:${lineNumber}: ${MODULE_MARKER.end} ${name} without a ${MODULE_MARKER.begin}.`);
      blockStart = null;
      return;
    }
    if (blockStart === null && kind === null) kept.push(line);
  });
  if (blockStart !== null) throw new MarkerError(`${label}:${blockStart}: ${MODULE_MARKER.begin} ${name} has no ${MODULE_MARKER.end}.`);
  return { text: kept.join(LINE_BREAK), removedLines: lines.length - kept.length };
}

function belongsToPrefix(key, prefix) {
  return key === prefix || key.startsWith(`${prefix}${I18N_KEY_SEPARATOR}`);
}

/**
 * Removes the keys under `prefix` from a translation file: the top-level object `prefix` (keys `prefix.*`) and any
 * flat top-level key that starts with `prefix.`. The file keeps its key order, indent, line endings, BOM and final
 * line break; when nothing matches the text is returned unchanged.
 * @param {string} text the JSON file content
 * @param {string} prefix e.g. "serviceRequests"
 * @param {string} [label] the file name used in error messages
 * @returns {{ text: string, removedKeys: readonly string[] }}
 */
export function removeI18nPrefix(text, prefix, label = 'text') {
  const bom = text.startsWith(UTF8_BOM) ? UTF8_BOM : '';
  const body = text.slice(bom.length);
  let parsed;
  try {
    parsed = JSON.parse(body);
  } catch (error) {
    throw new MarkerError(`${label} is not valid JSON: ${error.message}`);
  }
  if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new MarkerError(`${label} is not a JSON object.`);
  const removedKeys = Object.keys(parsed).filter((key) => belongsToPrefix(key, prefix));
  if (removedKeys.length === 0) return { text, removedKeys: NO_KEYS };
  for (const key of removedKeys) delete parsed[key];
  const indent = LEADING_INDENT.exec(body)?.[1] ?? JSON_INDENT;
  const eol = body.includes(CRLF) ? CRLF : LINE_BREAK;
  const finalBreak = body.endsWith(LINE_BREAK) ? eol : '';
  const serialized = JSON.stringify(parsed, null, indent).split(LINE_BREAK).join(eol);
  return { text: `${bom}${serialized}${finalBreak}`, removedKeys };
}
