// The small Markdown subset a feature spec uses: a YAML front matter of scalars and string lists, level-2 sections,
// pipe tables and ordered lists. Zero dependencies on purpose (the validator runs before `npm ci`).
import {
  CODE_FENCE,
  ESCAPED_PIPE,
  ESCAPED_PIPE_PLACEHOLDER,
  FRONT_MATTER_ENTRY,
  FRONT_MATTER_FENCE,
  FRONT_MATTER_FLOW_LIST,
  FRONT_MATTER_IGNORABLE,
  FRONT_MATTER_LIST_ITEM,
  H2_HEADING,
  HTML_COMMENT_CLOSE,
  HTML_COMMENT_OPEN,
  ORDERED_LIST_ITEM,
  TABLE_CELL_DELIMITER,
  TABLE_ROW,
  TABLE_SEPARATOR_CELL,
} from './constants.mjs';

const LINE_BREAK = /\r?\n/;
const QUOTED = /^(["'])(.*)\1$/;
const LIST_SEPARATOR = ',';
const FIRST_LINE_NUMBER = 1;

/** Splits text into lines (CRLF or LF). */
export function splitLines(text) {
  return text.split(LINE_BREAK);
}

function unquote(value) {
  const trimmed = value.trim();
  const match = QUOTED.exec(trimmed);
  return match ? match[2] : trimmed;
}

/**
 * Reads the front matter at the top of the spec.
 * @param {string[]} lines all lines of the spec
 * @returns {{ found: boolean, values: Map<string, string|string[]>, errors: { line: number, text: string }[], bodyStart: number }}
 *   bodyStart is the 0-based index of the first line after the closing fence.
 */
export function parseFrontMatter(lines) {
  const values = new Map();
  const errors = [];
  if (lines[0]?.trim() !== FRONT_MATTER_FENCE) return { found: false, values, errors, bodyStart: 0 };
  const close = lines.findIndex((line, index) => index > 0 && line.trim() === FRONT_MATTER_FENCE);
  if (close < 0) return { found: false, values, errors, bodyStart: 0 };
  let currentList = null;
  for (let index = 1; index < close; index += 1) {
    const line = lines[index];
    const item = FRONT_MATTER_LIST_ITEM.exec(line);
    if (item && currentList) {
      currentList.push(unquote(item[1]));
      continue;
    }
    if (FRONT_MATTER_IGNORABLE.test(line)) continue;
    const entry = FRONT_MATTER_ENTRY.exec(line);
    if (!entry) {
      errors.push({ line: index + FIRST_LINE_NUMBER, text: line });
      currentList = null;
      continue;
    }
    const raw = (entry[2] ?? '').trim();
    const flow = FRONT_MATTER_FLOW_LIST.exec(raw);
    if (flow) {
      values.set(entry[1], flow[1].trim() === '' ? [] : flow[1].split(LIST_SEPARATOR).map(unquote));
      currentList = null;
    } else if (raw === '') {
      currentList = [];
      values.set(entry[1], currentList);
    } else {
      values.set(entry[1], unquote(raw));
      currentList = null;
    }
  }
  return { found: true, values, errors, bodyStart: close + 1 };
}

/**
 * Splits the body into level-2 sections. Headings inside fenced code blocks are ignored.
 * @returns {{ name: string, line: number, lines: { text: string, line: number }[] }[]}
 */
export function parseSections(lines, bodyStart) {
  const sections = [];
  let current = null;
  let inFence = false;
  for (let index = bodyStart; index < lines.length; index += 1) {
    const text = lines[index];
    const line = index + FIRST_LINE_NUMBER;
    if (CODE_FENCE.test(text)) inFence = !inFence;
    const heading = inFence ? null : H2_HEADING.exec(text);
    if (heading) {
      current = { name: heading[1].trim(), line, lines: [] };
      sections.push(current);
    } else if (current) {
      current.lines.push({ text, line, inFence });
    }
  }
  return sections;
}

/** Removes HTML comments from one line; returns the remaining text and whether a comment is still open. */
function stripComments(text, inComment) {
  let rest = text;
  let open = inComment;
  let kept = '';
  while (rest.length > 0) {
    const marker = open ? HTML_COMMENT_CLOSE : HTML_COMMENT_OPEN;
    const at = rest.indexOf(marker);
    if (at < 0) {
      if (!open) kept += rest;
      break;
    }
    if (!open) kept += rest.slice(0, at);
    rest = rest.slice(at + marker.length);
    open = !open;
  }
  return { kept, open };
}

/** The lines of a section that carry content: not blank and not inside an HTML comment. */
export function contentLines(section) {
  const lines = [];
  let inComment = false;
  for (const entry of section.lines) {
    const { kept, open } = stripComments(entry.text, inComment);
    inComment = open;
    if (kept.trim() !== '') lines.push({ ...entry, text: kept });
  }
  return lines;
}

/** Splits one table row into trimmed cells (`\|` stays a literal pipe). */
export function splitRow(text) {
  let inner = text.trim().replaceAll(ESCAPED_PIPE, ESCAPED_PIPE_PLACEHOLDER);
  if (inner.startsWith(TABLE_CELL_DELIMITER)) inner = inner.slice(TABLE_CELL_DELIMITER.length);
  if (inner.endsWith(TABLE_CELL_DELIMITER)) inner = inner.slice(0, -TABLE_CELL_DELIMITER.length);
  return inner.split(TABLE_CELL_DELIMITER).map((cell) => cell.replaceAll(ESCAPED_PIPE_PLACEHOLDER, TABLE_CELL_DELIMITER).trim());
}

function isSeparatorRow(cells) {
  return cells.length > 0 && cells.every((cell) => TABLE_SEPARATOR_CELL.test(cell));
}

/**
 * The first pipe table of a section (outside code fences).
 * @returns {{ header: string[], headerLine: number, rows: { cells: string[], line: number }[] } | null}
 */
export function firstTable(section) {
  const tableLines = [];
  for (const entry of section.lines) {
    const isRow = !entry.inFence && TABLE_ROW.test(entry.text);
    if (isRow) tableLines.push(entry);
    else if (tableLines.length > 0) break;
  }
  const minimumTableLines = 2;
  if (tableLines.length < minimumTableLines) return null;
  const separator = splitRow(tableLines[1].text);
  if (!isSeparatorRow(separator)) return null;
  return {
    header: splitRow(tableLines[0].text),
    headerLine: tableLines[0].line,
    rows: tableLines.slice(minimumTableLines).map((entry) => ({ cells: splitRow(entry.text), line: entry.line })),
  };
}

/** The ordered-list items of a section (outside code fences), with their line numbers. */
export function orderedListItems(section) {
  const items = [];
  for (const entry of section.lines) {
    if (entry.inFence) continue;
    const match = ORDERED_LIST_ITEM.exec(entry.text);
    if (match) items.push({ text: match[1].trim(), line: entry.line });
  }
  return items;
}
