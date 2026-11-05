// Insertions into C# files the generators own (or that follow the module anatomy): permission keys, catalog entries,
// DI registrations and using directives. Pure functions over text; every one is idempotent.
//
// Files the scaffolder wrote carry `// scaffold:...` markers and new lines go right above the marker, at its
// indentation. Hand-written modules (Access, Identity) have no markers, so each insertion has a fallback anchor.
import { CATALOG_LIST_END, CLASS_BODY_END, CRLF, Markers, NEWLINE, REGISTRATION_RETURN } from './constants.mjs';

const USING_LINE = /^using\s+[\w.]+;\s*$/gm;
const NAMESPACE_LINE = /^namespace\s/m;
const REGEX_SPECIAL = /[.*+?^${}()|[\]\\]/g;

/** Raised when an insertion would contradict what the file already holds; nothing is written. */
export class ScaffoldRefusal extends Error {}

function newlineOf(text) {
  return text.includes(CRLF) ? CRLF : NEWLINE;
}

function escapeRegex(value) {
  return value.replace(REGEX_SPECIAL, '\\$&');
}

/** Inserts `lines` (unindented) above the first line holding `marker`, at the marker's indentation; null if absent. */
function insertAboveMarker(text, marker, lines) {
  const markerLine = new RegExp(`^([ \\t]*)${escapeRegex(marker)}[^\\r\\n]*$`, 'm').exec(text);
  if (!markerLine) return null;
  return insertAt(text, markerLine.index, markerLine[1], lines);
}

/** Inserts `lines` above the line the anchor regex matches (group 1 = its indentation, plus `extraIndent`). */
function insertAboveAnchor(text, anchor, lines, extraIndent) {
  const match = anchor.exec(text);
  if (!match) return null;
  return insertAt(text, match.index, (match[1] ?? '') + extraIndent, lines);
}

function insertAt(text, index, indent, lines) {
  const eol = newlineOf(text);
  const block = lines.map((line) => (line.length === 0 ? eol : `${indent}${line}${eol}`)).join('');
  return text.slice(0, index) + block + text.slice(index);
}

const MEMBER_INDENT = '    ';

/**
 * Adds `public const string <constName> = "<value>";` to a constants class (<Module>PermissionKeys,
 * <Module>ErrorCodes) above `marker`, or before the class's closing brace in a file without the marker.
 * @returns {{ text: string, added: boolean }}
 */
export function addStringConstant(text, { constName, value, summary, marker }) {
  const existing = new RegExp(`const\\s+string\\s+${escapeRegex(constName)}\\s*=\\s*"([^"]*)"`).exec(text);
  if (existing) {
    if (existing[1] === value) return { text, added: false };
    throw new ScaffoldRefusal(`The constant ${constName} already holds "${existing[1]}", not "${value}".`);
  }
  if (text.includes(`"${value}"`)) throw new ScaffoldRefusal(`The value "${value}" is already declared under another constant name.`);
  const lines = [`/// <summary>${summary}</summary>`, `public const string ${constName} = "${value}";`, ''];
  const result = insertAboveMarker(text, marker, lines)
    ?? insertAboveAnchor(text, CLASS_BODY_END, ['', ...lines.slice(0, -1)], MEMBER_INDENT);
  if (result === null) throw new ScaffoldRefusal('The constants file has no class body to add to.');
  return { text: result, added: true };
}

/**
 * Adds `new(Key: <Module>PermissionKeys.<constName>, Description: "..."),` to an IPermissionCatalogSource list.
 * @returns {{ text: string, added: boolean }}
 */
export function addCatalogEntry(text, { keysClass, constName, description }) {
  const reference = `${keysClass}.${constName}`;
  if (new RegExp(`${escapeRegex(reference)}\\b`).test(text)) return { text, added: false };
  const line = `new(Key: ${reference}, Description: "${description.replaceAll('"', '\\"')}"),`;
  const result = insertAboveMarker(text, Markers.permissionCatalog, [line])
    ?? insertAboveAnchor(text, CATALOG_LIST_END, [line], MEMBER_INDENT);
  if (result === null) throw new ScaffoldRefusal('The permission catalog source has no collection expression ("];") to add to.');
  return { text: result, added: true };
}

/**
 * Adds DI registration statements to a <Module>ModuleRegistration Add method (each line once).
 * @param {string[]} statements full statements, e.g. "services.AddScoped<GetWidget>();"
 * @returns {{ text: string, added: string[] }}
 */
export function addRegistrations(text, statements) {
  const missing = statements.filter((statement) => !text.includes(statement));
  if (missing.length === 0) return { text, added: [] };
  const result = insertAboveMarker(text, Markers.registrations, missing)
    ?? insertAboveAnchor(text, REGISTRATION_RETURN, missing, '');
  if (result === null) throw new ScaffoldRefusal('The module registration has no marker and no "return services;" to add to.');
  return { text: result, added: missing };
}

/**
 * Adds `using <namespace>;` directives that are missing, keeping the block sorted when it already is.
 * @returns {{ text: string, added: string[] }}
 */
export function addUsings(text, namespaces) {
  const present = [...text.matchAll(USING_LINE)].map((match) => match[0].trim());
  const missing = [...new Set(namespaces)].map((ns) => `using ${ns};`).filter((line) => !present.includes(line));
  if (missing.length === 0) return { text, added: [] };
  const eol = newlineOf(text);
  const all = [...present, ...missing].sort((left, right) => left.localeCompare(right, 'en', { sensitivity: 'base' }));
  if (present.length === 0) {
    const namespaceAt = NAMESPACE_LINE.exec(text)?.index ?? 0;
    return { text: text.slice(0, namespaceAt) + all.join(eol) + eol + eol + text.slice(namespaceAt), added: missing };
  }
  const first = text.indexOf(present[0]);
  const lastLine = present[present.length - 1];
  const end = text.indexOf(lastLine, first) + lastLine.length;
  return { text: text.slice(0, first) + all.join(eol) + text.slice(end), added: missing };
}
