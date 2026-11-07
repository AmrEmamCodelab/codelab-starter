// Segments split a source file into code, string literals and comments. Every character of the file belongs to
// exactly one segment, in order. Anything a scanner cannot classify stays code, so a scanner mistake can only turn a
// comments-only edit into a false alarm, never let a code change pass.

export const SegmentKind = Object.freeze({
  CODE: 'code',
  STRING: 'string',
  COMMENT: 'comment',
});

export class SegmentList {
  #items = [];
  #codeStart;

  constructor(start) {
    this.#codeStart = start;
  }

  literal(start, end) {
    this.#flushCode(start);
    this.#items.push({ kind: SegmentKind.STRING, start, end });
    this.#codeStart = end;
  }

  comment(start, end, isDoc = false) {
    this.#flushCode(start);
    this.#items.push({ kind: SegmentKind.COMMENT, start, end, isDoc });
    this.#codeStart = end;
  }

  finish(end) {
    this.#flushCode(end);
    return this.#items;
  }

  #flushCode(upto) {
    if (upto > this.#codeStart) {
      this.#items.push({ kind: SegmentKind.CODE, start: this.#codeStart, end: upto });
    }
  }
}
