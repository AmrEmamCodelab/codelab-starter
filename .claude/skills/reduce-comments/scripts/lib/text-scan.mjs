// Character-level helpers shared by the dialect scanners.

export const NO_MATCH = -1;

const LINE_FEED = '\n';
const CARRIAGE_RETURN = '\r';
const BACKSLASH = '\\';
const ESCAPE_WIDTH = 2;
const IDENTIFIER_CHAR = /[\p{L}\p{N}_$]/u;

export function isLineBreak(ch) {
  return ch === LINE_FEED || ch === CARRIAGE_RETURN;
}

export function isIdentifierChar(ch) {
  return ch !== undefined && IDENTIFIER_CHAR.test(ch);
}

export function isWhitespace(ch) {
  return ch !== undefined && ch.trim() === '';
}

/** Index of the line break that ends the line holding `from` (or the end of the text). */
export function lineEnd(text, from) {
  let index = from;
  while (index < text.length && !isLineBreak(text[index])) {
    index += 1;
  }
  return index;
}

/** Index just past `closer`, searching from `from`; an unterminated construct runs to the end of the text. */
export function delimitedEnd(text, from, closer) {
  const at = text.indexOf(closer, from);
  return at === NO_MATCH ? text.length : at + closer.length;
}

/**
 * Index just past a quoted run whose opening quote is at `openIndex`.
 * backslashEscapes: `\x` never closes. doubledQuoteEscapes: two quotes in a row are one literal quote.
 */
export function quotedEnd(text, openIndex, { backslashEscapes, doubledQuoteEscapes }) {
  const quote = text[openIndex];
  let index = openIndex + 1;
  while (index < text.length) {
    const ch = text[index];
    if (backslashEscapes && ch === BACKSLASH) {
      index += ESCAPE_WIDTH;
      continue;
    }
    if (ch === quote) {
      if (doubledQuoteEscapes && text[index + 1] === quote) {
        index += ESCAPE_WIDTH;
        continue;
      }
      return index + 1;
    }
    index += 1;
  }
  return text.length;
}

/** Number of consecutive `ch` characters starting at `from`. */
export function runLength(text, from, ch) {
  let index = from;
  while (text[index] === ch) {
    index += 1;
  }
  return index - from;
}

/** True when only spaces or tabs sit between the start of the line and `index`. */
export function startsLine(text, index) {
  let cursor = index - 1;
  while (cursor >= 0 && !isLineBreak(text[cursor])) {
    if (!isWhitespace(text[cursor])) {
      return false;
    }
    cursor -= 1;
  }
  return true;
}
