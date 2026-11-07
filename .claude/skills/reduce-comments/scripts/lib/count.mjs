// Counts comment units: what a reader has to read, not raw lines. A run of whole-line single-line comments of the
// same flavour (doc or plain) on consecutive lines is one unit; a block comment is one unit; a trailing comment on a
// code line is one unit.
import { SegmentKind } from './segments.mjs';
import { countLineBreaks } from './normalize.mjs';
import { startsLine } from './text-scan.mjs';

const LINE_BREAK = '\n';
const FIRST_LINE = 1;

/** @returns {{ units: number, docUnits: number, commentLines: number, fileLines: number }} */
export function countComments(text, segments) {
  const lineOf = lineLocator(text);
  const commentLines = new Set();
  let units = 0;
  let docUnits = 0;
  let previous = null;
  for (const segment of segments.filter((item) => item.kind === SegmentKind.COMMENT)) {
    const current = describe(text, segment, lineOf);
    for (let line = current.startLine; line <= current.endLine; line += 1) {
      commentLines.add(line);
    }
    if (!continuesRun(previous, current)) {
      units += 1;
      docUnits += current.isDoc ? 1 : 0;
    }
    previous = current;
  }
  return { units, docUnits, commentLines: commentLines.size, fileLines: fileLineCount(text) };
}

function describe(text, segment, lineOf) {
  const startLine = lineOf(segment.start);
  const endLine = startLine + countLineBreaks(text.slice(segment.start, segment.end));
  return {
    startLine,
    endLine,
    isDoc: segment.isDoc === true,
    wholeLine: startsLine(text, segment.start),
  };
}

function continuesRun(previous, current) {
  return previous !== null
    && previous.wholeLine
    && current.wholeLine
    && previous.startLine === previous.endLine
    && current.startLine === current.endLine
    && current.startLine === previous.endLine + 1
    && current.isDoc === previous.isDoc;
}

/** Returns a function mapping an offset to its 1-based line; offsets must be asked for in ascending order. */
function lineLocator(text) {
  let offset = 0;
  let line = FIRST_LINE;
  return (target) => {
    for (; offset < target; offset += 1) {
      line += text[offset] === LINE_BREAK ? 1 : 0;
    }
    return line;
  };
}

function fileLineCount(text) {
  if (text.length === 0) {
    return 0;
  }
  return countLineBreaks(text) + (text.endsWith(LINE_BREAK) ? 0 : 1);
}
