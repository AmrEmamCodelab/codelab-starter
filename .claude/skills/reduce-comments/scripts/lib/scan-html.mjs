// HTML / Angular template scanner: `<!-- -->` comments. Quoted attribute values (Angular bindings included) and the
// quoted strings inside `{{ }}` interpolations are literals, so a `<!--` inside them is never read as a comment.
// The bodies of raw-text elements (script, style, textarea, title) are code: a `<!--` there is not a comment.
import { SegmentList } from './segments.mjs';
import { NO_MATCH, delimitedEnd, quotedEnd } from './text-scan.mjs';

const COMMENT_OPENER = '<!--';
const COMMENT_CLOSER = '-->';
const INTERPOLATION_OPENER = '{{';
const INTERPOLATION_CLOSER = '}}';
const TAG_OPENER = '<';
const TAG_CLOSER = '>';
const END_TAG_PREFIX = '</';
const QUOTES = new Set(['"', "'"]);
const TAG_NAME_PATTERN = /^[A-Za-z][\w:-]*/;
const TAG_START_PATTERN = /[A-Za-z/!?]/;
const RAW_TEXT_ELEMENTS = new Set(['script', 'style', 'textarea', 'title']);

export function scanHtml(text) {
  const segments = new SegmentList(0);
  let index = 0;
  while (index < text.length) {
    index = nextIndex(text, index, segments);
  }
  return segments.finish(text.length);
}

function nextIndex(text, index, segments) {
  if (text.startsWith(COMMENT_OPENER, index)) {
    const end = delimitedEnd(text, index + COMMENT_OPENER.length, COMMENT_CLOSER);
    segments.comment(index, end);
    return end;
  }
  if (text.startsWith(INTERPOLATION_OPENER, index)) {
    return interpolationEnd(text, index + INTERPOLATION_OPENER.length, segments);
  }
  if (text[index] === TAG_OPENER && TAG_START_PATTERN.test(text[index + 1] ?? '')) {
    return tagEnd(text, index, segments);
  }
  return index + 1;
}

function interpolationEnd(text, from, segments) {
  let index = from;
  while (index < text.length) {
    if (text.startsWith(INTERPOLATION_CLOSER, index)) {
      return index + INTERPOLATION_CLOSER.length;
    }
    index = QUOTES.has(text[index]) ? pushQuoted(text, index, true, segments) : index + 1;
  }
  return text.length;
}

/** Scans a tag to its `>`; after an opening raw-text element tag, also skips the element body. */
function tagEnd(text, openIndex, segments) {
  let index = openIndex + 1;
  while (index < text.length) {
    if (text[index] === TAG_CLOSER) {
      return rawTextBodyEnd(text, openIndex, index + 1);
    }
    index = QUOTES.has(text[index]) ? pushQuoted(text, index, false, segments) : index + 1;
  }
  return text.length;
}

function rawTextBodyEnd(text, openIndex, bodyStart) {
  const name = (text.slice(openIndex + 1).match(TAG_NAME_PATTERN)?.[0] ?? '').toLowerCase();
  if (!RAW_TEXT_ELEMENTS.has(name)) {
    return bodyStart;
  }
  const closing = text.toLowerCase().indexOf(`${END_TAG_PREFIX}${name}`, bodyStart);
  return closing === NO_MATCH ? text.length : closing;
}

function pushQuoted(text, index, backslashEscapes, segments) {
  const end = quotedEnd(text, index, { backslashEscapes, doubledQuoteEscapes: false });
  segments.literal(index, end);
  return end;
}
