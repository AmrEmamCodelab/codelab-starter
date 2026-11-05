// A small tokenizer for Angular templates: just enough HTML to see elements, their attributes and their nesting.
// It skips comments, interpolations ({{ }}), block parameters (@if (...), @for (...), @let ...;) and the bodies of
// script/style elements, and reads quoted attribute values whole, so `>`, `<` or a tag name inside a value, a comment
// or an expression is never taken for markup. It never throws: unclosed constructs run to the end of the text.

/** Elements that never have children or a closing tag. */
const VOID_ELEMENTS = new Set(['area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr']);
/** Elements whose content is raw text up to their closing tag. */
const RAW_TEXT_ELEMENTS = new Set(['script', 'style']);

const Markup = Object.freeze({
  commentOpen: '<!--',
  commentClose: '-->',
  interpolationOpen: '{{',
  interpolationClose: '}}',
  tagOpen: '<',
  closingTagOpen: '</',
  declarationOpen: '<!',
  tagClose: '>',
  selfClose: '/>',
  equals: '=',
  blockStart: '@',
  letBlock: 'let',
  letEnd: ';',
  parenOpen: '(',
  parenClose: ')',
});
const QUOTES = new Set(['"', "'"]);
const NAME_START = /[A-Za-z]/;
const WHITESPACE = /\s/;
const TAG_NAME_END = /[\s/>]/;
const BLOCK_NAME_CHAR = /[A-Za-z ]/;
const LINE_BREAK = '\n';
const FIRST = 1;
const HALVES = 2;

/** Maps text offsets to 1-based lines and columns. */
function positionFinder(text) {
  const lineStarts = [0];
  for (let index = text.indexOf(LINE_BREAK); index !== -1; index = text.indexOf(LINE_BREAK, index + FIRST)) {
    lineStarts.push(index + FIRST);
  }
  return (offset) => {
    let low = 0;
    let high = lineStarts.length - FIRST;
    while (low < high) {
      const middle = Math.ceil((low + high) / HALVES);
      if (lineStarts[middle] <= offset) low = middle;
      else high = middle - FIRST;
    }
    return { line: low + FIRST, column: offset - lineStarts[low] + FIRST };
  };
}

/** The offset just past `close` searched from `from`, or the end of the text. */
function skipPast(text, from, close) {
  const end = text.indexOf(close, from);
  return end === -1 ? text.length : end + close.length;
}

/** Skips a balanced `( ... )` starting at `start` (quotes respected); returns the offset after it. */
function skipParentheses(text, start) {
  let depth = 0;
  for (let index = start; index < text.length; index++) {
    const char = text[index];
    if (QUOTES.has(char)) {
      index = skipPast(text, index + FIRST, char) - FIRST;
    } else if (char === Markup.parenOpen) {
      depth++;
    } else if (char === Markup.parenClose && --depth === 0) {
      return index + FIRST;
    }
  }
  return text.length;
}

/** `@if (...)`, `@else if (...)`, `@for (...)`, `@let x = ...;`: returns the offset after the parameters. */
function skipBlockParameters(text, start) {
  let index = start + Markup.blockStart.length;
  while (index < text.length && BLOCK_NAME_CHAR.test(text[index])) index++;
  const name = text.slice(start + Markup.blockStart.length, index).trim();
  if (name.split(' ')[0] === Markup.letBlock) return skipPast(text, index, Markup.letEnd);
  return text[index] === Markup.parenOpen ? skipParentheses(text, index) : index;
}

function readAttributeValue(text, start) {
  let index = start;
  while (index < text.length && WHITESPACE.test(text[index])) index++;
  const quote = text[index];
  if (QUOTES.has(quote)) {
    const end = text.indexOf(quote, index + FIRST);
    const stop = end === -1 ? text.length : end;
    return { value: text.slice(index + FIRST, stop), next: Math.min(stop + FIRST, text.length) };
  }
  const from = index;
  while (index < text.length && !WHITESPACE.test(text[index]) && text[index] !== Markup.tagClose) index++;
  return { value: text.slice(from, index), next: index };
}

/** Reads the attributes of an open tag from `start` (just past the name) to its `>` or `/>`. */
function readAttributes(text, start) {
  const attributes = new Map();
  let index = start;
  while (index < text.length) {
    while (index < text.length && WHITESPACE.test(text[index])) index++;
    if (text.startsWith(Markup.selfClose, index)) return { attributes, selfClosing: true, next: index + Markup.selfClose.length };
    if (index >= text.length || text[index] === Markup.tagClose) return { attributes, selfClosing: false, next: index + FIRST };
    const from = index;
    while (index < text.length && !WHITESPACE.test(text[index]) && text[index] !== Markup.equals
      && text[index] !== Markup.tagClose && !text.startsWith(Markup.selfClose, index)) index++;
    const name = text.slice(from, index);
    let after = index;
    while (after < text.length && WHITESPACE.test(text[after])) after++;
    if (text[after] === Markup.equals) {
      const { value, next } = readAttributeValue(text, after + Markup.equals.length);
      attributes.set(name, value);
      index = next;
    } else {
      attributes.set(name, null);
    }
  }
  return { attributes, selfClosing: false, next: text.length };
}

function readTagName(text, start) {
  let index = start;
  while (index < text.length && !TAG_NAME_END.test(text[index])) index++;
  return { name: text.slice(start, index).toLowerCase(), next: index };
}

/**
 * The element events of a template, in order: `{ type: 'open', name, attributes, selfClosing, line, column }` and
 * `{ type: 'close', name }`. Tag names are lower-cased; attribute names keep their case (Angular is case-sensitive);
 * a valueless attribute has the value `null`.
 */
export function scanTemplate(text) {
  const positionOf = positionFinder(text);
  const events = [];
  let index = 0;
  while (index < text.length) {
    if (text.startsWith(Markup.commentOpen, index)) {
      index = skipPast(text, index + Markup.commentOpen.length, Markup.commentClose);
    } else if (text.startsWith(Markup.interpolationOpen, index)) {
      index = skipPast(text, index + Markup.interpolationOpen.length, Markup.interpolationClose);
    } else if (text[index] === Markup.blockStart && NAME_START.test(text[index + FIRST] ?? '')) {
      index = skipBlockParameters(text, index);
    } else if (text.startsWith(Markup.closingTagOpen, index) && NAME_START.test(text[index + Markup.closingTagOpen.length] ?? '')) {
      const { name, next } = readTagName(text, index + Markup.closingTagOpen.length);
      events.push({ type: 'close', name });
      index = skipPast(text, next, Markup.tagClose);
    } else if (text[index] === Markup.tagOpen && NAME_START.test(text[index + FIRST] ?? '')) {
      index = readOpenTag(text, index, positionOf, events);
    } else if (text.startsWith(Markup.declarationOpen, index)) {
      index = skipPast(text, index, Markup.tagClose);
    } else {
      index++;
    }
  }
  return events;
}

function readOpenTag(text, start, positionOf, events) {
  const { name, next } = readTagName(text, start + Markup.tagOpen.length);
  const tag = readAttributes(text, next);
  events.push({ type: 'open', name, attributes: tag.attributes, selfClosing: tag.selfClosing || VOID_ELEMENTS.has(name), ...positionOf(start) });
  if (!RAW_TEXT_ELEMENTS.has(name) || tag.selfClosing) return tag.next;
  const closing = text.toLowerCase().indexOf(`${Markup.closingTagOpen}${name}`, tag.next);
  return closing === -1 ? text.length : closing;
}
