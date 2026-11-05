// A small lexer for TypeScript sources, enough for text-level checks: it finds comments, string literals, template
// literals (with ${} nesting) and regular-expression literals, so a check can look at code without comments and at
// each string literal on its own. It never throws: an unclosed construct runs to the end of the source.

const Lexer = Object.freeze({
  lineComment: '//',
  blockCommentOpen: '/*',
  blockCommentClose: '*/',
  slash: '/',
  backslash: '\\',
  backtick: '`',
  substitutionOpen: '${',
  braceOpen: '{',
  braceClose: '}',
  classOpen: '[',
  classClose: ']',
  lineBreak: '\n',
  space: ' ',
});
const STRING_QUOTES = new Set(['"', "'"]);
const IDENTIFIER_START = /[A-Za-z_$]/;
const IDENTIFIER_CHAR = /[\w$]/;
const WHITESPACE = /\s/;
/** After one of these (or at the start) a `/` starts a regular expression, not a division. */
const REGEX_PRECEDERS = new Set(['', '(', ',', '=', ':', '[', '!', '&', '|', '?', '{', '}', ';', '+', '-', '*', '%', '<', '>', '~', '^']);
const REGEX_KEYWORDS = new Set(['return', 'typeof', 'case', 'do', 'else', 'in', 'of', 'new', 'delete', 'void', 'throw', 'yield', 'await']);
const NEXT = 1;
/** The previous token was a value, so a following `/` divides. */
const OPERAND = 'operand';

function commentEnd(source, index) {
  if (source.startsWith(Lexer.lineComment, index)) {
    const end = source.indexOf(Lexer.lineBreak, index);
    return end === -1 ? source.length : end;
  }
  if (source.startsWith(Lexer.blockCommentOpen, index)) {
    const end = source.indexOf(Lexer.blockCommentClose, index + Lexer.blockCommentOpen.length);
    return end === -1 ? source.length : end + Lexer.blockCommentClose.length;
  }
  return null;
}

function quotedEnd(source, start) {
  const quote = source[start];
  for (let index = start + NEXT; index < source.length; index++) {
    if (source[index] === Lexer.backslash) index++;
    else if (source[index] === quote || source[index] === Lexer.lineBreak) return index + NEXT;
  }
  return source.length;
}

function templateLiteralEnd(source, start) {
  for (let index = start + NEXT; index < source.length; index++) {
    if (source[index] === Lexer.backslash) index++;
    else if (source[index] === Lexer.backtick) return index + NEXT;
    else if (source.startsWith(Lexer.substitutionOpen, index)) index = substitutionEnd(source, index + Lexer.substitutionOpen.length) - NEXT;
  }
  return source.length;
}

function substitutionEnd(source, start) {
  let depth = 1;
  let index = start;
  while (index < source.length) {
    const skipped = commentEnd(source, index) ?? literalEnd(source, index);
    if (skipped !== null) {
      index = skipped;
      continue;
    }
    if (source[index] === Lexer.braceOpen) depth++;
    else if (source[index] === Lexer.braceClose && --depth === 0) return index + NEXT;
    index++;
  }
  return source.length;
}

function literalEnd(source, index) {
  if (STRING_QUOTES.has(source[index])) return quotedEnd(source, index);
  if (source[index] === Lexer.backtick) return templateLiteralEnd(source, index);
  return null;
}

function regexEnd(source, start) {
  let inClass = false;
  for (let index = start + NEXT; index < source.length; index++) {
    const char = source[index];
    if (char === Lexer.backslash) index++;
    else if (char === Lexer.lineBreak) return index;
    else if (char === Lexer.classOpen) inClass = true;
    else if (char === Lexer.classClose) inClass = false;
    else if (char === Lexer.slash && !inClass) return index + NEXT;
  }
  return source.length;
}

/** Replaces every character but line breaks with a space, so offsets and lines stay valid. */
export function blank(text) {
  return text.replace(/[^\n]/g, Lexer.space);
}

/**
 * Lexes a TypeScript source.
 * @returns {{ code: string, strings: { offset: number, end: number, raw: string, templateLiteral: boolean, substitution: boolean }[] }}
 *   `code` is the source with every comment blanked (same length and lines); `strings` lists each string or template
 *   literal: `offset` of its opening quote, `end` just past its closing quote, `raw` the text between the quotes
 *   (escapes kept), and whether it is a template literal with a `${}` substitution.
 */
export function lexScript(source) {
  let code = '';
  const strings = [];
  let previous = '';
  let index = 0;
  while (index < source.length) {
    const comment = commentEnd(source, index);
    if (comment !== null) {
      code += blank(source.slice(index, comment));
      index = comment;
      continue;
    }
    const literal = literalEnd(source, index);
    if (literal !== null) {
      const templateLiteral = source[index] === Lexer.backtick;
      const raw = source.slice(index + NEXT, Math.max(index + NEXT, literal - NEXT));
      strings.push({ offset: index, end: literal, raw, templateLiteral, substitution: templateLiteral && raw.includes(Lexer.substitutionOpen) });
      code += source.slice(index, literal);
      index = literal;
      previous = OPERAND;
      continue;
    }
    const char = source[index];
    if (char === Lexer.slash && (REGEX_PRECEDERS.has(previous) || REGEX_KEYWORDS.has(previous))) {
      const end = regexEnd(source, index);
      code += source.slice(index, end);
      index = end;
      previous = OPERAND;
      continue;
    }
    if (IDENTIFIER_START.test(char)) {
      let end = index;
      while (end < source.length && IDENTIFIER_CHAR.test(source[end])) end++;
      previous = source.slice(index, end);
      code += previous;
      index = end;
      continue;
    }
    if (!WHITESPACE.test(char)) previous = char;
    code += char;
    index++;
  }
  return { code, strings };
}

/** `code` with the given `[offset, end)` ranges blanked. */
export function blankRanges(code, ranges) {
  let result = code;
  for (const { offset, end } of ranges) result = result.slice(0, offset) + blank(result.slice(offset, end)) + result.slice(end);
  return result;
}
