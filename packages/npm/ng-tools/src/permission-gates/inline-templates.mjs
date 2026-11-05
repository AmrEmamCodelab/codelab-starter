// Finds the inline templates of a TypeScript file: the string literal of every `template:` property. A small lexer
// skips comments, strings, template literals (with ${} nesting) and regular-expression literals, so `template:`
// inside any of them is never taken for a property, and `templateUrl` is a different word. A `template:` that names a
// constant (`template: TEMPLATE`) is resolved to that constant's string literal when the same file declares it
// (`const TEMPLATE = \`...\``); a constant from another file cannot be resolved and is not scanned.

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
  colon: ':',
  dot: '.',
  classOpen: '[',
  classClose: ']',
  lineBreak: '\n',
});
const TEMPLATE_PROPERTY = 'template';
const DECLARATION_KEYWORDS = new Set(['const', 'let', 'var']);
const ASSIGN = '=';
/** After `=` these make `==`, `===` or `=>`, not an assignment. */
const NOT_ASSIGNMENT_NEXT = new Set(['=', '>']);
const STRING_QUOTES = new Set(['"', "'"]);
const IDENTIFIER_START = /[A-Za-z_$]/;
const IDENTIFIER_CHAR = /[\w$]/;
const WHITESPACE = /\s/;
/** After one of these (or at the start) a `/` starts a regular expression, not a division. */
const REGEX_PRECEDERS = new Set(['', '(', ',', '=', ':', '[', '!', '&', '|', '?', '{', '}', ';', '+', '-', '*', '%', '<', '>', '~', '^']);
const REGEX_KEYWORDS = new Set(['return', 'typeof', 'case', 'do', 'else', 'in', 'of', 'new', 'delete', 'void', 'throw', 'yield', 'await']);
const ESCAPE_IN_QUOTES = /\\(.)/gs;
const ESCAPE_IN_BACKTICKS = /\\([`$\\])/g;
const NEWLINE_ESCAPE = 'n';
const NEXT = 1;
/** The previous token was a value (a literal), so a following `/` divides. */
const OPERAND = Symbol('operand');

function skipQuoted(source, start) {
  const quote = source[start];
  for (let index = start + NEXT; index < source.length; index++) {
    if (source[index] === Lexer.backslash) index++;
    else if (source[index] === quote || source[index] === Lexer.lineBreak) return index + NEXT;
  }
  return source.length;
}

function skipTemplateLiteral(source, start) {
  for (let index = start + NEXT; index < source.length; index++) {
    if (source[index] === Lexer.backslash) index++;
    else if (source[index] === Lexer.backtick) return index + NEXT;
    else if (source.startsWith(Lexer.substitutionOpen, index)) index = skipSubstitution(source, index + Lexer.substitutionOpen.length) - NEXT;
  }
  return source.length;
}

/** Skips a `${ ... }` body from `start` (just past `${`); returns the offset after its closing brace. */
function skipSubstitution(source, start) {
  let depth = 1;
  let index = start;
  while (index < source.length) {
    const skipped = skipTrivia(source, index) ?? skipLiteral(source, index);
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

/** Skips a comment at `index`; returns the offset after it, or null when none starts there. */
function skipTrivia(source, index) {
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

/** Skips a string or template literal at `index`; returns the offset after it, or null. */
function skipLiteral(source, index) {
  if (STRING_QUOTES.has(source[index])) return skipQuoted(source, index);
  if (source[index] === Lexer.backtick) return skipTemplateLiteral(source, index);
  return null;
}

function skipRegex(source, start) {
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

function skipWhitespaceAndComments(source, start) {
  let index = start;
  for (;;) {
    while (index < source.length && WHITESPACE.test(source[index])) index++;
    const skipped = skipTrivia(source, index);
    if (skipped === null) return index;
    index = skipped;
  }
}

function lineOf(source, offset) {
  let line = 1;
  for (let index = source.indexOf(Lexer.lineBreak); index !== -1 && index < offset; index = source.indexOf(Lexer.lineBreak, index + NEXT)) line++;
  return line;
}

function unescape(raw, delimiter) {
  if (delimiter === Lexer.backtick) return raw.replace(ESCAPE_IN_BACKTICKS, '$1');
  return raw.replace(ESCAPE_IN_QUOTES, (_match, char) => (char === NEWLINE_ESCAPE ? Lexer.lineBreak : char));
}

/** The string literal at `start` as `{ text, line, end }`, or null when none starts there. */
function literalAt(source, start) {
  const end = skipLiteral(source, start);
  if (end === null) return null;
  const delimiter = source[start];
  const raw = source.slice(start + NEXT, Math.max(start + NEXT, end - NEXT));
  return { text: unescape(raw, delimiter), line: lineOf(source, start), end };
}

/** The identifier at `start`, or null. */
function identifierAt(source, start) {
  if (!IDENTIFIER_START.test(source[start] ?? '')) return null;
  let end = start;
  while (end < source.length && IDENTIFIER_CHAR.test(source[end])) end++;
  return { name: source.slice(start, end), end };
}

/**
 * The value after `template` at `afterWord`: `{ text, line, end }` for a literal, `{ reference, end }` for a
 * constant name, or null when it is not a template property.
 */
function templateValueAt(source, afterWord) {
  const colon = skipWhitespaceAndComments(source, afterWord);
  if (source[colon] !== Lexer.colon) return null;
  const start = skipWhitespaceAndComments(source, colon + NEXT);
  const literal = literalAt(source, start);
  if (literal !== null) return literal;
  const identifier = identifierAt(source, start);
  return identifier === null ? null : { reference: identifier.name, end: identifier.end };
}

/** The string literal assigned in `const NAME = '...'` with `afterName` just past NAME, or null. */
function declaredLiteralAt(source, afterName) {
  const equals = skipWhitespaceAndComments(source, afterName);
  if (source[equals] !== ASSIGN || NOT_ASSIGNMENT_NEXT.has(source[equals + NEXT])) return null;
  return literalAt(source, skipWhitespaceAndComments(source, equals + NEXT));
}

/**
 * Every inline template of a TypeScript source as `{ text, line }`, where `line` (1-based) is the line of the
 * literal's opening quote, so template line N is source line `line + N - 1`.
 */
export function extractInlineTemplates(source) {
  const templates = [];
  const references = new Set();
  const constants = new Map();
  let previous = '';
  let index = 0;
  while (index < source.length) {
    const skipped = skipTrivia(source, index) ?? skipLiteral(source, index);
    if (skipped !== null) {
      previous = STRING_QUOTES.has(source[index]) || source[index] === Lexer.backtick ? OPERAND : previous;
      index = skipped;
      continue;
    }
    const char = source[index];
    if (char === Lexer.slash && (REGEX_PRECEDERS.has(previous) || REGEX_KEYWORDS.has(previous))) {
      index = skipRegex(source, index);
      previous = OPERAND;
      continue;
    }
    if (IDENTIFIER_START.test(char)) {
      let end = index;
      while (end < source.length && IDENTIFIER_CHAR.test(source[end])) end++;
      const word = source.slice(index, end);
      if (DECLARATION_KEYWORDS.has(previous)) {
        const declared = declaredLiteralAt(source, end);
        if (declared !== null && !constants.has(word)) constants.set(word, { text: declared.text, line: declared.line });
      }
      const value = word === TEMPLATE_PROPERTY && previous !== Lexer.dot ? templateValueAt(source, end) : null;
      if (value?.reference !== undefined) references.add(value.reference);
      else if (value !== null) templates.push({ text: value.text, line: value.line });
      index = value === null ? end : value.end;
      previous = value === null ? word : OPERAND;
      continue;
    }
    if (!WHITESPACE.test(char)) previous = char;
    index++;
  }
  for (const reference of references) {
    const constant = constants.get(reference);
    if (constant !== undefined) templates.push(constant);
  }
  return templates.sort((first, second) => first.line - second.line);
}
