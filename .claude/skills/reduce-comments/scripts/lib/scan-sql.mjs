// SQL Server (T-SQL) scanner: `--` line comments and nested `/* */` block comments; 'strings' ('' escapes a quote,
// N'...' included), "quoted identifiers" and [bracketed identifiers] (]] escapes a bracket) are literals, so
// 'a--b' and [we--ird] never hold a comment. `//` is not a comment in T-SQL.
import { SegmentList } from './segments.mjs';
import { NO_MATCH, lineEnd, quotedEnd } from './text-scan.mjs';

const LINE_OPENER = '--';
const BLOCK_OPENER = '/*';
const BLOCK_CLOSER = '*/';
const SINGLE_QUOTE = "'";
const DOUBLE_QUOTE = '"';
const OPEN_BRACKET = '[';
const CLOSE_BRACKET = ']';
const TOKEN_WIDTH = 2;

export function scanSql(text) {
  const segments = new SegmentList(0);
  let index = 0;
  while (index < text.length) {
    const commentEnd = commentEndAt(text, index);
    if (commentEnd !== NO_MATCH) {
      segments.comment(index, commentEnd);
      index = commentEnd;
      continue;
    }
    const literalEnd = literalEndAt(text, index);
    if (literalEnd !== NO_MATCH) {
      segments.literal(index, literalEnd);
      index = literalEnd;
      continue;
    }
    index += 1;
  }
  return segments.finish(text.length);
}

function commentEndAt(text, index) {
  if (text.startsWith(LINE_OPENER, index)) {
    return lineEnd(text, index);
  }
  if (text.startsWith(BLOCK_OPENER, index)) {
    return nestedBlockEnd(text, index);
  }
  return NO_MATCH;
}

/** T-SQL block comments nest: `/* a /* b *\/ c *\/` is one comment. */
function nestedBlockEnd(text, openIndex) {
  let depth = 0;
  let index = openIndex;
  while (index < text.length) {
    if (text.startsWith(BLOCK_OPENER, index)) {
      depth += 1;
      index += TOKEN_WIDTH;
    } else if (text.startsWith(BLOCK_CLOSER, index)) {
      depth -= 1;
      index += TOKEN_WIDTH;
      if (depth === 0) {
        return index;
      }
    } else {
      index += 1;
    }
  }
  return text.length;
}

function literalEndAt(text, index) {
  const ch = text[index];
  if (ch === SINGLE_QUOTE || ch === DOUBLE_QUOTE) {
    return quotedEnd(text, index, { backslashEscapes: false, doubledQuoteEscapes: true });
  }
  if (ch === OPEN_BRACKET) {
    return bracketEnd(text, index);
  }
  return NO_MATCH;
}

function bracketEnd(text, openIndex) {
  let index = openIndex + 1;
  while (index < text.length) {
    if (text[index] === CLOSE_BRACKET) {
      if (text[index + 1] !== CLOSE_BRACKET) {
        return index + 1;
      }
      index += TOKEN_WIDTH;
    } else {
      index += 1;
    }
  }
  return text.length;
}
