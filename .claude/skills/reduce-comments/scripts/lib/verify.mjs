// The comments-only proof: both files are scanned, comments are stripped and code whitespace is normalized
// (normalize.mjs); the results must be identical. The first difference is reported with its line in each file.
import { scan } from './dialects.mjs';
import { normalize } from './normalize.mjs';

export const VerifyOutcome = Object.freeze({
  COMMENTS_ONLY: 'comments-only',
  CODE_CHANGED: 'code-changed',
});

const NO_DIFFERENCE = -1;
const CONTEXT_BEFORE = 40;
const CONTEXT_AFTER = 40;
const SHOWN_LINE_BREAK = '\\n';
const LINE_BREAK = '\n';
const UNKNOWN_LINE = null;

/**
 * @returns {{ outcome: string } | { outcome: string, baseline: { line: number|null, context: string },
 *   edited: { line: number|null, context: string } }}
 */
export function verifyCommentsOnly({ baselineText, editedText, dialect }) {
  const baseline = normalize(baselineText, scan(baselineText, dialect));
  const edited = normalize(editedText, scan(editedText, dialect));
  const at = firstDifference(baseline.chars, edited.chars);
  if (at === NO_DIFFERENCE) {
    return { outcome: VerifyOutcome.COMMENTS_ONLY };
  }
  return {
    outcome: VerifyOutcome.CODE_CHANGED,
    baseline: describeAt(baseline, at),
    edited: describeAt(edited, at),
  };
}

export function firstDifference(left, right) {
  const shared = Math.min(left.length, right.length);
  for (let index = 0; index < shared; index += 1) {
    if (left[index] !== right[index]) {
      return index;
    }
  }
  return left.length === right.length ? NO_DIFFERENCE : shared;
}

function describeAt(normalized, at) {
  const { chars, lines } = normalized;
  const lastLine = lines.length > 0 ? lines[lines.length - 1] : UNKNOWN_LINE;
  const line = at < lines.length ? lines[at] : lastLine;
  const context = chars.slice(Math.max(0, at - CONTEXT_BEFORE), at + CONTEXT_AFTER).join('');
  return { line, context: context.split(LINE_BREAK).join(SHOWN_LINE_BREAK) };
}
