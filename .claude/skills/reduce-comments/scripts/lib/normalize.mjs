// Turns a scanned file into the text that must stay identical when only comments change:
// - comments are dropped and act as whitespace (a line break when the comment spans lines, which keeps
//   automatic-semicolon-insertion boundaries in script visible);
// - whitespace runs in code collapse to one space, or one line break when the run holds a line break;
// - string literals are kept byte for byte.
// Every output character carries the 1-based source line it came from, for the failure report.
import { SegmentKind } from './segments.mjs';
import { isLineBreak, isWhitespace } from './text-scan.mjs';

const SPACE = ' ';
const LINE_BREAK = '\n';
const FIRST_LINE = 1;

class NormalizedBuilder {
  chars = [];
  lines = [];
  #pending = null;

  addSeparator(separator) {
    this.#pending = this.#pending === LINE_BREAK ? LINE_BREAK : separator;
  }

  addChar(ch, line) {
    if (this.#pending !== null && this.chars.length > 0) {
      this.chars.push(this.#pending);
      this.lines.push(line);
    }
    this.#pending = null;
    this.chars.push(ch);
    this.lines.push(line);
  }
}

/** @returns {{ chars: string[], lines: number[] }} chars[i] came from source line lines[i]. */
export function normalize(text, segments) {
  const builder = new NormalizedBuilder();
  let line = FIRST_LINE;
  for (const segment of segments) {
    const body = text.slice(segment.start, segment.end);
    if (segment.kind === SegmentKind.COMMENT) {
      builder.addSeparator(body.includes(LINE_BREAK) ? LINE_BREAK : SPACE);
      line += countLineBreaks(body);
      continue;
    }
    line = segment.kind === SegmentKind.STRING ? addVerbatim(builder, body, line) : addCode(builder, body, line);
  }
  return { chars: builder.chars, lines: builder.lines };
}

function addVerbatim(builder, body, firstLine) {
  let line = firstLine;
  for (const ch of body) {
    builder.addChar(ch, line);
    line += ch === LINE_BREAK ? 1 : 0;
  }
  return line;
}

function addCode(builder, body, firstLine) {
  let line = firstLine;
  for (const ch of body) {
    if (isWhitespace(ch)) {
      builder.addSeparator(isLineBreak(ch) ? LINE_BREAK : SPACE);
    } else {
      builder.addChar(ch, line);
    }
    line += ch === LINE_BREAK ? 1 : 0;
  }
  return line;
}

export function countLineBreaks(body) {
  let count = 0;
  for (const ch of body) {
    count += ch === LINE_BREAK ? 1 : 0;
  }
  return count;
}
