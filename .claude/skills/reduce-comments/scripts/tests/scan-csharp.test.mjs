import assert from 'node:assert/strict';
import { test } from 'node:test';
import { scanCSharp } from '../lib/scan-csharp.mjs';
import { SegmentKind } from '../lib/segments.mjs';
import { assertTiles, commentsOf, stringsOf } from './helpers.mjs';

function scan(text) {
  const segments = scanCSharp(text);
  assertTiles(assert, text, segments);
  return segments;
}

test('a // inside a regular string is not a comment', () => {
  const text = 'var url = "http://host/a"; // trailing note';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['"http://host/a"']);
  assert.deepEqual(commentsOf(text, segments), ['// trailing note']);
});

test('a /* inside a string is not a comment, and escaped quotes do not end the string', () => {
  const text = 'var s = "a \\" /* b */ c"; /* real */';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['"a \\" /* b */ c"']);
  assert.deepEqual(commentsOf(text, segments), ['/* real */']);
});

test('a verbatim string ends at a quote after a backslash and keeps doubled quotes', () => {
  const text = 'var p = @"C:\\dir\\"; var q = @"say ""//hi"""; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['@"C:\\dir\\"', '@"say ""//hi"""']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('an interpolated string with a nested string holding // and } in a hole is one literal', () => {
  const text = 'var m = $"{(ok ? "//" : "}")} done {{x}}"; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['$"{(ok ? "//" : "}")} done {{x}}"']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('verbatim interpolated strings in both prefix orders are literals', () => {
  const text = 'var a = $@"{x}"" //"; var b = @$"{y} /* z */"; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['$@"{x}"" //"', '@$"{y} /* z */"']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('raw and interpolated raw strings are literals, even across lines', () => {
  const text = 'var r = """\n  // not a comment\n  """;\nvar i = $$"""{{x}} /* y */ "quoted" """; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), [
    '"""\n  // not a comment\n  """',
    '$$"""{{x}} /* y */ "quoted" """',
  ]);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('char literals holding a quote or an escaped apostrophe do not open a string', () => {
  const text = "var q = '\"'; var a = '\\''; // c";
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ["'\"'", "'\\''"]);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('/// and /** are documentation; //// and /**/ are not', () => {
  const text = '/// <summary>Doc</summary>\n//// plain\n/** doc */\n/**/\nclass A {}';
  const segments = scan(text).filter((segment) => segment.kind === SegmentKind.COMMENT);
  assert.deepEqual(segments.map((segment) => segment.isDoc), [true, false, true, false]);
});

test('a verbatim identifier is code and an unterminated block comment runs to the end', () => {
  const text = 'var @class = 1; /* open';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), []);
  assert.deepEqual(commentsOf(text, segments), ['/* open']);
});
