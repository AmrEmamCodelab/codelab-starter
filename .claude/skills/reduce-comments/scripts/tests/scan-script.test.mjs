import assert from 'node:assert/strict';
import { test } from 'node:test';
import { regexAllowedAfter, scanScript } from '../lib/scan-script.mjs';
import { SegmentKind } from '../lib/segments.mjs';
import { assertTiles, commentsOf, stringsOf } from './helpers.mjs';

function scan(text) {
  const segments = scanScript(text);
  assertTiles(assert, text, segments);
  return segments;
}

test('// and /* inside quoted strings are not comments', () => {
  const text = "const u = 'http://a'; const b = \"a/*b*/\"; // c";
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ["'http://a'", '"a/*b*/"']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('a template literal with // and /* text and a hole is one literal', () => {
  const text = 'const t = `x // ${y} /* z */`; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['`x // ${y} /* z */`']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('a hole holding a nested template and an object literal does not end the template early', () => {
  const text = 'const t = `a${ `b//` }c${ { k: 1 }.k }d`; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['`a${ `b//` }c${ { k: 1 }.k }d`']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('a regex literal holding /* or // is a literal, with its class and flags', () => {
  const text = 'const r = /\\/*foo/g; const k = /[/*]/; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['/\\/*foo/g', '/[/*]/']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('a regex after return is a literal', () => {
  const text = 'function f(s) { return /x\\/\\//.test(s); } // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), ['/x\\/\\//']);
  assert.deepEqual(commentsOf(text, segments), ['// c']);
});

test('division after an identifier or a closing parenthesis stays code', () => {
  const text = 'const a = b / c; const d = (e) / 2 /* half */; // c';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), []);
  assert.deepEqual(commentsOf(text, segments), ['/* half */', '// c']);
});

test('documented limit: a regex right after ) is read as division (REFERENCE.md, known limits)', () => {
  const text = 'if (ok) /a/.test(s);';
  const segments = scan(text);
  assert.deepEqual(stringsOf(text, segments), []);
  assert.deepEqual(commentsOf(text, segments), []);
});

test('/** is documentation, // is not', () => {
  const text = '/** Doc. */\nexport const a = 1; // plain';
  const segments = scan(text).filter((segment) => segment.kind === SegmentKind.COMMENT);
  assert.deepEqual(segments.map((segment) => segment.isDoc), [true, false]);
});

test('regexAllowedAfter tells regex from division by the previous token', () => {
  const text = 'return x ) ] = (';
  assert.equal(regexAllowedAfter(text, null), true);
  assert.equal(regexAllowedAfter(text, { ch: 'n', index: 5 }), true, 'after the keyword return');
  assert.equal(regexAllowedAfter(text, { ch: 'x', index: 7 }), false, 'after an identifier');
  assert.equal(regexAllowedAfter(text, { ch: ')', index: 9 }), false);
  assert.equal(regexAllowedAfter(text, { ch: ']', index: 11 }), false);
  assert.equal(regexAllowedAfter(text, { ch: '=', index: 13 }), true);
  assert.equal(regexAllowedAfter(text, { ch: '(', index: 15 }), true);
});
