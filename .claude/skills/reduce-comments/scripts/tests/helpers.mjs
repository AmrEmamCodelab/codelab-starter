// Shared helpers for the comment-diff tests.
import { SegmentKind } from '../lib/segments.mjs';

/** The text of every segment of one kind, in order. */
export function textsOf(text, segments, kind) {
  return segments.filter((segment) => segment.kind === kind).map((segment) => text.slice(segment.start, segment.end));
}

export const commentsOf = (text, segments) => textsOf(text, segments, SegmentKind.COMMENT);
export const stringsOf = (text, segments) => textsOf(text, segments, SegmentKind.STRING);

/** Segments must tile the text: contiguous, in order, covering every character. */
export function assertTiles(assert, text, segments) {
  let expectedStart = 0;
  for (const segment of segments) {
    assert.equal(segment.start, expectedStart, `segment starts at ${segment.start}, expected ${expectedStart}`);
    assert.ok(segment.end > segment.start, 'segments are never empty');
    expectedStart = segment.end;
  }
  assert.equal(expectedStart, text.length, 'segments cover the whole text');
}
