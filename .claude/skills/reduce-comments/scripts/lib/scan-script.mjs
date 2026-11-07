// TypeScript / JavaScript scanner: `//`, `/* */` comments; '' and "" strings; template literals with ${} holes
// (scanned as script, so a backtick or brace inside a hole never ends the template early); regex literals, told apart
// from division by the previous significant token. When in doubt a `/` is read as a regex, because reading a regex
// as code could mistake a `/*` inside it for a comment.
import { SegmentList } from './segments.mjs';
import { cStyleCommentEnd, isDocBlock } from './c-style-comments.mjs';
import { NO_MATCH, isIdentifierChar, isLineBreak, isWhitespace, quotedEnd } from './text-scan.mjs';

const DOUBLE_QUOTE = '"';
const SINGLE_QUOTE = "'";
const BACKTICK = '`';
const SLASH = '/';
const BACKSLASH = '\\';
const OPEN_BRACE = '{';
const CLOSE_BRACE = '}';
const OPEN_CLASS = '[';
const CLOSE_CLASS = ']';
const HOLE_OPENER = '${';
const ESCAPE_WIDTH = 2;
const AFTER_LITERAL = Symbol('after-literal');
// A `/` after one of these closes an operand, so it is division.
const OPERAND_CLOSERS = new Set([')', ']']);
// A `/` after one of these keywords starts a regex (`return /x/`).
const REGEX_KEYWORDS = new Set([
  'return', 'typeof', 'instanceof', 'in', 'of', 'new', 'delete', 'void', 'throw', 'case', 'do', 'else', 'yield',
  'await',
]);

export function scanScript(text) {
  return scanRange(text, 0, false).segments;
}

function scanRange(text, start, stopAtClosingBrace) {
  const segments = new SegmentList(start);
  const state = { depth: 0, previous: null };
  let index = start;
  while (index < text.length) {
    const literalEnd = literalEndAt(text, index, state.previous);
    if (literalEnd !== NO_MATCH) {
      segments.literal(index, literalEnd);
      state.previous = { ch: AFTER_LITERAL, index: literalEnd - 1 };
      index = literalEnd;
      continue;
    }
    const commentEnd = cStyleCommentEnd(text, index);
    if (commentEnd !== NO_MATCH) {
      segments.comment(index, commentEnd, isDocBlock(text, index));
      index = commentEnd;
      continue;
    }
    if (stopAtClosingBrace && text[index] === CLOSE_BRACE && state.depth === 0) {
      return { segments: segments.finish(index), end: index + 1 };
    }
    trackCodeChar(text, index, state);
    index += 1;
  }
  return { segments: segments.finish(text.length), end: text.length };
}

function trackCodeChar(text, index, state) {
  const ch = text[index];
  if (ch === OPEN_BRACE) {
    state.depth += 1;
  } else if (ch === CLOSE_BRACE) {
    state.depth -= 1;
  }
  if (!isWhitespace(ch)) {
    state.previous = { ch, index };
  }
}

function literalEndAt(text, index, previous) {
  const ch = text[index];
  if (ch === SINGLE_QUOTE || ch === DOUBLE_QUOTE) {
    return quotedEnd(text, index, { backslashEscapes: true, doubledQuoteEscapes: false });
  }
  if (ch === BACKTICK) {
    return templateEnd(text, index);
  }
  if (ch === SLASH && cStyleCommentEnd(text, index) === NO_MATCH && regexAllowedAfter(text, previous)) {
    return regexEnd(text, index);
  }
  return NO_MATCH;
}

function templateEnd(text, openIndex) {
  let index = openIndex + 1;
  while (index < text.length) {
    const ch = text[index];
    if (ch === BACKSLASH) {
      index += ESCAPE_WIDTH;
    } else if (ch === BACKTICK) {
      return index + 1;
    } else if (text.startsWith(HOLE_OPENER, index)) {
      index = scanRange(text, index + HOLE_OPENER.length, true).end;
    } else {
      index += 1;
    }
  }
  return text.length;
}

/** True when a `/` following `previous` (the last significant code character, or null) starts a regex literal. */
export function regexAllowedAfter(text, previous) {
  if (previous === null) {
    return true;
  }
  if (previous.ch === AFTER_LITERAL || OPERAND_CLOSERS.has(previous.ch)) {
    return false;
  }
  if (isIdentifierChar(previous.ch)) {
    return REGEX_KEYWORDS.has(wordEndingAt(text, previous.index));
  }
  return true;
}

function wordEndingAt(text, lastIndex) {
  let start = lastIndex;
  while (start > 0 && isIdentifierChar(text[start - 1])) {
    start -= 1;
  }
  return text.slice(start, lastIndex + 1);
}

/** End of the regex literal (flags included) opening at `openIndex`; NO_MATCH when the line ends first. */
function regexEnd(text, openIndex) {
  let inClass = false;
  let index = openIndex + 1;
  while (index < text.length && !isLineBreak(text[index])) {
    const ch = text[index];
    if (ch === BACKSLASH) {
      index += ESCAPE_WIDTH;
      continue;
    }
    if (ch === SLASH && !inClass) {
      return flagsEnd(text, index + 1);
    }
    inClass = classStateAfter(ch, inClass);
    index += 1;
  }
  return NO_MATCH;
}

function classStateAfter(ch, inClass) {
  if (ch === OPEN_CLASS) {
    return true;
  }
  return ch === CLOSE_CLASS ? false : inClass;
}

function flagsEnd(text, from) {
  let index = from;
  while (isIdentifierChar(text[index])) {
    index += 1;
  }
  return index;
}
