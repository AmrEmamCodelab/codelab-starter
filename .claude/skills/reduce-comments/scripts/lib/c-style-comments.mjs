// `//` line comments and `/* */` block comments, shared by the C# and TypeScript/JavaScript scanners.
import { NO_MATCH, delimitedEnd, lineEnd } from './text-scan.mjs';

const LINE_OPENER = '//';
const BLOCK_OPENER = '/*';
const BLOCK_CLOSER = '*/';
const DOC_BLOCK_OPENER = '/**';
const EMPTY_BLOCK = '/**/';
const DOC_LINE_OPENER = '///';
const NOT_DOC_LINE_OPENER = '////';

/** End index of the comment starting at `index`, or NO_MATCH when no comment starts there. */
export function cStyleCommentEnd(text, index) {
  if (text.startsWith(LINE_OPENER, index)) {
    return lineEnd(text, index);
  }
  if (text.startsWith(BLOCK_OPENER, index)) {
    return delimitedEnd(text, index + BLOCK_OPENER.length, BLOCK_CLOSER);
  }
  return NO_MATCH;
}

/** `/** *\/` is documentation in both languages. */
export function isDocBlock(text, index) {
  return text.startsWith(DOC_BLOCK_OPENER, index) && !text.startsWith(EMPTY_BLOCK, index);
}

/** `///` is C# XML documentation (`////` is a plain comment). */
export function isDocLine(text, index) {
  return text.startsWith(DOC_LINE_OPENER, index) && !text.startsWith(NOT_DOC_LINE_OPENER, index);
}
