// Markdown reading for the gate: lines with their code-fence context, inline code spans and table columns.
// Deliberately small: only what the checks need, no full CommonMark parser.

const FENCE = /^ {0,3}(`{3,}|~{3,})(.*)$/;
const CODE_SPAN = /(`+)(.+?)\1(?!`)/g;
const TABLE_ROW = /^\s*\|/;
const TABLE_SEPARATOR = /^\s*\|?\s*:?-{3,}:?\s*(?:\|\s*:?-{3,}:?\s*)*\|?\s*$/;
const BACKTICK = '`';
const CELL_SEPARATOR = '|';
const ESCAPE = '\\';
const SPAN_PADDING = ' ';
const PARAGRAPH_JOINER = ' ';

/**
 * @typedef {{ number: number, text: string, fence: { id: number, language: string, intro: string } | null }} MarkdownLine
 * fence is null for prose; fence lines carry the block id (so a "cd" in one block never leaks into the next) and the
 * intro: the prose paragraph right before the block ("Then, from `frontend/starter-web`:").
 */

/**
 * Splits Markdown into lines; fence delimiter lines themselves are dropped.
 * @param {string} text
 * @returns {MarkdownLine[]}
 */
export function readMarkdownLines(text) {
  const lines = [];
  let open = null;
  let blockCount = 0;
  // The last prose paragraph: blank lines end it, but it stays the intro until the next prose line starts a new one.
  let paragraph = [];
  let paragraphEnded = false;
  text.split('\n').forEach((raw, index) => {
    const line = raw.replace(/\r$/, '');
    const fence = FENCE.exec(line);
    if (open === null) {
      if (fence) {
        blockCount += 1;
        const language = (fence[2].trim().split(/\s+/)[0] ?? '').toLowerCase();
        open = { marker: fence[1], id: blockCount, language, intro: paragraph.join(PARAGRAPH_JOINER) };
        paragraph = [];
        paragraphEnded = false;
        return;
      }
      if (line.trim() === '') {
        paragraphEnded = true;
      } else {
        paragraph = paragraphEnded ? [line] : [...paragraph, line];
        paragraphEnded = false;
      }
      lines.push({ number: index + 1, text: line, fence: null });
      return;
    }
    if (fence && fence[1][0] === open.marker[0] && fence[1].length >= open.marker.length && fence[2].trim() === '') {
      open = null;
      return;
    }
    lines.push({ number: index + 1, text: line, fence: { id: open.id, language: open.language, intro: open.intro } });
  });
  return lines;
}

/**
 * Inline code spans of a prose line. One leading and trailing space is stripped when both are present (CommonMark).
 * @returns {{ content: string, start: number, end: number }[]} start/end are 0-based offsets of the whole span
 */
export function readCodeSpans(lineText) {
  return [...lineText.matchAll(CODE_SPAN)].map((match) => {
    const inner = match[2];
    const padded = inner.length > 2 && inner.startsWith(SPAN_PADDING) && inner.endsWith(SPAN_PADDING);
    return { content: padded ? inner.slice(1, -1) : inner, start: match.index, end: match.index + match[0].length };
  });
}

/**
 * Cell ranges of a table row: a "|" inside a code span or escaped with "\" does not split.
 * @returns {{ start: number, end: number }[]}
 */
export function splitTableCells(rowText) {
  const separators = [];
  let openTicks = 0;
  for (let index = 0; index < rowText.length; index += 1) {
    const char = rowText[index];
    if (char === BACKTICK) {
      let run = 1;
      while (rowText[index + run] === BACKTICK) run += 1;
      if (openTicks === 0) openTicks = run;
      else if (openTicks === run) openTicks = 0;
      index += run - 1;
    } else if (char === CELL_SEPARATOR && openTicks === 0 && rowText[index - 1] !== ESCAPE) {
      separators.push(index);
    }
  }
  const cells = [];
  const boundaries = rowText.trimStart().startsWith(CELL_SEPARATOR) ? separators : [-1, ...separators];
  for (let index = 0; index < boundaries.length; index += 1) {
    const start = boundaries[index] + 1;
    const end = index + 1 < boundaries.length ? boundaries[index + 1] : rowText.length;
    if (index + 1 === boundaries.length && rowText.slice(start).trim() === '') break;
    cells.push({ start, end });
  }
  return cells;
}

/**
 * For every prose line that is a table data row, the lower-cased header text of each cell, keyed by line number.
 * @param {MarkdownLine[]} lines
 * @returns {Map<number, { cells: { start: number, end: number }[], headers: string[] }>}
 */
export function readTableColumns(lines) {
  const rows = new Map();
  let headers = null;
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    if (line.fence !== null || !TABLE_ROW.test(line.text)) {
      headers = null;
      continue;
    }
    const next = lines[index + 1];
    if (headers === null && next && next.fence === null && next.number === line.number + 1 && TABLE_SEPARATOR.test(next.text)) {
      headers = splitTableCells(line.text).map((cell) => line.text.slice(cell.start, cell.end).trim().toLowerCase());
      index += 1;
      continue;
    }
    if (headers !== null) rows.set(line.number, { cells: splitTableCells(line.text), headers });
  }
  return rows;
}

/** The header of the column a 0-based offset falls in, or null. */
export function columnHeaderAt(row, offset) {
  if (!row) return null;
  const position = row.cells.findIndex((cell) => offset >= cell.start && offset < cell.end);
  return position === -1 ? null : (row.headers[position] ?? null);
}
