// Pure reads and edits of a record type module's C# sources for new-transition: find the workflow classes, read their
// constants, add a constant to a class, and add an entry to the Statuses or Rules list of the workflow class. Every
// function takes and returns text; none touches the disk.
import { CRLF, NEWLINE } from '../constants.mjs';
import { CSharp, ListProperties, SourcePatterns, WorkflowMarkers } from './constants.mjs';

const REGEX_SPECIAL = /[.*+?^${}()|[\]\\]/g;
const CONTINUES_LIST = /(?:,|\[|\/\/[^\r\n]*)\s*$/;
const ALL_LIST = /(\bAll\s*\{\s*get;\s*\}\s*=\s*)\[([^\]]*)\]/;
const LIST_OPEN_LINE = /\[[ \t]*\r?$/gm;

/** The line ending a file uses. */
export function newlineOf(text) {
  return text.includes(CRLF) ? CRLF : NEWLINE;
}

/** A regex-safe form of a literal. */
export function escapeRegex(value) {
  return value.replace(REGEX_SPECIAL, '\\$&');
}

/** True when `candidate` is `namespace` itself or one of its parents (a using of it would be unnecessary). */
export function isSameOrParentNamespace(candidate, namespace) {
  return Boolean(namespace) && (candidate === namespace || namespace.startsWith(`${candidate}.`));
}

/** A C# string literal. */
export function csharpString(value) {
  return `"${value.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;
}

/** Text safe inside an XML doc comment. */
export function xmlText(value) {
  return value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
}

/**
 * Every class `pattern` (a /g regex, group 1 = class name) finds in `files`.
 * @param {{ path: string, text: string }[]} files
 * @returns {{ path: string, name: string, namespace: string | null }[]}
 */
export function findClasses(files, pattern) {
  return files.flatMap((file) => [...file.text.matchAll(new RegExp(pattern.source, 'g'))].map((match) => ({
    path: file.path,
    name: match[1],
    namespace: SourcePatterns.namespace.exec(file.text)?.[1] ?? null,
  })));
}

/**
 * The body of class `name`: from its declaration to its closing brace (the first line after the declaration that is
 * a `}` at the declaration's indentation). Null when the file does not declare it.
 * @returns {{ start: number, close: number, indent: string } | null} `close` is the index of the closing brace line
 */
export function classSpan(text, name) {
  const declaration = new RegExp(`^([ \\t]*)(?:[\\w]+\\s+)*class\\s+${escapeRegex(name)}\\b`, 'm').exec(text);
  if (!declaration) return null;
  const indent = declaration[1];
  const closing = new RegExp(`^${indent}\\}[ \\t]*$`, 'mg');
  closing.lastIndex = declaration.index;
  const close = closing.exec(text);
  return close ? { start: declaration.index, close: close.index, indent } : null;
}

/** The `const int` (or `const string`) members of class `name`, in file order. */
export function constantsOf(text, name, pattern) {
  const span = classSpan(text, name);
  if (!span) return [];
  const body = text.slice(span.start, span.close);
  return [...body.matchAll(new RegExp(pattern.source, 'g'))].map((match) => ({ name: match[1], value: match[2] }));
}

/** The next free int value of a statuses class: the highest constant plus one (1 for an empty class). */
export function nextStatusValue(constants) {
  return constants.reduce((highest, constant) => Math.max(highest, Number(constant.value)), 0) + 1;
}

/** Inserts `lines` (relative indentation) just before the closing brace of class `name`, after an empty line. */
export function addClassMember(text, name, lines) {
  const span = classSpan(text, name);
  if (!span) return null;
  const eol = newlineOf(text);
  const memberIndent = span.indent + CSharp.indent;
  const before = text.slice(0, span.close);
  const opensEmpty = /\{\s*$/.test(before);
  const block = (opensEmpty ? '' : eol) + lines.map((line) => (line.length === 0 ? eol : `${memberIndent}${line}${eol}`)).join('');
  return before + block + text.slice(span.close);
}

function insertLines(text, index, indent, lines) {
  const eol = newlineOf(text);
  const block = lines.map((line) => (line.length === 0 ? eol : `${indent}${line}${eol}`)).join('');
  return text.slice(0, index) + block + text.slice(index);
}

/** Adds a trailing comma to the last non-blank line before `index` when the list would otherwise not continue. */
function ensurePreviousEntryContinues(text, index) {
  const before = text.slice(0, index);
  const trimmed = before.replace(/\s+$/, '');
  if (CONTINUES_LIST.test(trimmed)) return { text, index };
  return { text: `${trimmed},${before.slice(trimmed.length)}${text.slice(index)}`, index: index + 1 };
}

/**
 * Where an entry of the `kind` list goes: above its `// scaffold:workflow-<kind>` marker, or before the closing `];`
 * of the `Statuses` / `Rules` property's collection expression. Null when neither exists.
 * @param {'statuses' | 'rules'} kind
 * @returns {{ index: number, indent: string, regionStart: number } | null} `regionStart`: where the list opens
 */
export function listAnchor(text, kind) {
  const marker = new RegExp(`^([ \\t]*)${escapeRegex(WorkflowMarkers[kind])}[^\\r\\n]*$`, 'm').exec(text);
  if (marker) return { index: marker.index, indent: marker[1], regionStart: listOpenBefore(text, marker.index) };
  const property = new RegExp(`\\b${ListProperties[kind]}\\s*\\{\\s*get;\\s*\\}\\s*=\\s*\\[`, 'm').exec(text);
  if (!property) return null;
  const close = /^([ \t]*)\];/mg;
  close.lastIndex = property.index;
  const end = close.exec(text);
  return end ? { index: end.index, indent: end[1] + CSharp.indent, regionStart: property.index } : null;
}

function listOpenBefore(text, index) {
  const opens = [...text.slice(0, index).matchAll(LIST_OPEN_LINE)];
  return opens.length === 0 ? 0 : opens[opens.length - 1].index;
}

/** The text of the `kind` list so far (from its opening to the insertion point), or null without an anchor. */
export function listRegion(text, kind) {
  const anchor = listAnchor(text, kind);
  return anchor ? text.slice(anchor.regionStart, anchor.index) : null;
}

/** Inserts an entry (lines with relative indentation) into the `kind` list; null when the list has no anchor. */
export function addListEntry(text, kind, lines) {
  const anchor = listAnchor(text, kind);
  if (!anchor) return null;
  const continued = ensurePreviousEntryContinues(text, anchor.index);
  return insertLines(continued.text, continued.index, anchor.indent, lines);
}

/** The name of the class whose body holds `index` (the innermost one declared before it and still open). */
export function enclosingClass(text, index) {
  const declarations = [...text.slice(0, index).matchAll(/^[ \t]*(?:[\w]+\s+)*class\s+(\w+)\b/mg)];
  for (let at = declarations.length - 1; at >= 0; at -= 1) {
    const span = classSpan(text, declarations[at][1]);
    if (span && span.close > index) return declarations[at][1];
  }
  return null;
}

/** True when class `name` in `text` declares a member that `pattern` matches. */
export function classDeclares(text, name, pattern) {
  const span = classSpan(text, name);
  return span !== null && pattern.test(text.slice(span.start, span.close));
}

/**
 * Appends `member` to the `All { get; } = [...]` list of class `name` (a keys class that lists every key), once.
 * Returns the text unchanged when the class has no such list or already lists it.
 */
export function addToAllList(text, name, member) {
  const span = classSpan(text, name);
  if (!span) return text;
  const body = text.slice(span.start, span.close);
  const list = ALL_LIST.exec(body);
  if (!list) return text;
  const items = list[2].split(',').map((item) => item.trim()).filter((item) => item.length > 0);
  if (items.includes(member)) return text;
  const closeAt = span.start + list.index + list[0].length - 1;
  const trimmedEnd = text.slice(0, closeAt).replace(/\s+$/, '');
  const separator = trimmedEnd.endsWith('[') ? '' : ', ';
  return `${trimmedEnd}${separator}${member}${text.slice(closeAt)}`;
}
