// C# scanner: `//`, `///`, `/* */` comments; regular, verbatim (@""), interpolated ($"", $@"", @$""), raw
// ("""...""", $$"""...""") strings and char literals. Interpolation holes are scanned as C# so a quote or a brace
// inside a hole never ends the string early; the whole string, holes included, is one literal segment.
import { SegmentList } from './segments.mjs';
import { cStyleCommentEnd, isDocBlock, isDocLine } from './c-style-comments.mjs';
import { NO_MATCH, quotedEnd, runLength } from './text-scan.mjs';

const DOUBLE_QUOTE = '"';
const SINGLE_QUOTE = "'";
const DOLLAR = '$';
const AT = '@';
const OPEN_BRACE = '{';
const CLOSE_BRACE = '}';
const BACKSLASH = '\\';
const ESCAPE_WIDTH = 2;
const RAW_FENCE_MINIMUM = 3;

export function scanCSharp(text) {
  return scanRange(text, 0, false).segments;
}

/** Scans from `start`; with stopAtClosingBrace it ends just past the `}` that closes an interpolation hole. */
function scanRange(text, start, stopAtClosingBrace) {
  const segments = new SegmentList(start);
  let depth = 0;
  let index = start;
  while (index < text.length) {
    const literalEnd = literalEndAt(text, index);
    if (literalEnd !== NO_MATCH) {
      segments.literal(index, literalEnd);
      index = literalEnd;
      continue;
    }
    const commentEnd = cStyleCommentEnd(text, index);
    if (commentEnd !== NO_MATCH) {
      segments.comment(index, commentEnd, isDocLine(text, index) || isDocBlock(text, index));
      index = commentEnd;
      continue;
    }
    if (stopAtClosingBrace && text[index] === CLOSE_BRACE && depth === 0) {
      return { segments: segments.finish(index), end: index + 1 };
    }
    depth += braceDelta(text[index]);
    index += 1;
  }
  return { segments: segments.finish(text.length), end: text.length };
}

function braceDelta(ch) {
  if (ch === OPEN_BRACE) {
    return 1;
  }
  return ch === CLOSE_BRACE ? -1 : 0;
}

/** End index of the string or char literal starting at `index` (prefixes included), or NO_MATCH. */
function literalEndAt(text, index) {
  if (text[index] === SINGLE_QUOTE) {
    return quotedEnd(text, index, { backslashEscapes: true, doubledQuoteEscapes: false });
  }
  const prefix = readPrefix(text, index);
  if (text[prefix.quoteIndex] !== DOUBLE_QUOTE) {
    return NO_MATCH;
  }
  const fence = runLength(text, prefix.quoteIndex, DOUBLE_QUOTE);
  if (!prefix.verbatim && fence >= RAW_FENCE_MINIMUM) {
    return rawEnd(text, prefix.quoteIndex, fence);
  }
  if (prefix.interpolated) {
    return interpolatedEnd(text, prefix.quoteIndex, prefix.verbatim);
  }
  return quotedEnd(text, prefix.quoteIndex, {
    backslashEscapes: !prefix.verbatim,
    doubledQuoteEscapes: prefix.verbatim,
  });
}

function readPrefix(text, index) {
  let cursor = index;
  let interpolated = false;
  let verbatim = false;
  while (text[cursor] === DOLLAR || text[cursor] === AT) {
    interpolated ||= text[cursor] === DOLLAR;
    verbatim ||= text[cursor] === AT;
    cursor += 1;
  }
  return { quoteIndex: cursor, interpolated, verbatim };
}

/** A raw string closes at the first run of as many quotes as it opened with. */
function rawEnd(text, quoteIndex, fence) {
  const closer = DOUBLE_QUOTE.repeat(fence);
  const at = text.indexOf(closer, quoteIndex + fence);
  return at === NO_MATCH ? text.length : at + fence;
}

function interpolatedEnd(text, quoteIndex, verbatim) {
  let index = quoteIndex + 1;
  while (index < text.length) {
    const ch = text[index];
    if (!verbatim && ch === BACKSLASH) {
      index += ESCAPE_WIDTH;
    } else if (ch === DOUBLE_QUOTE) {
      if (!(verbatim && text[index + 1] === DOUBLE_QUOTE)) {
        return index + 1;
      }
      index += ESCAPE_WIDTH;
    } else if (ch === OPEN_BRACE) {
      index = text[index + 1] === OPEN_BRACE ? index + ESCAPE_WIDTH : scanRange(text, index + 1, true).end;
    } else {
      index += 1;
    }
  }
  return text.length;
}
