import assert from 'node:assert/strict';
import { test } from 'node:test';
import { Dialect } from '../lib/dialects.mjs';
import { VerifyOutcome, firstDifference, verifyCommentsOnly } from '../lib/verify.mjs';

function outcome(baselineText, editedText, dialect) {
  return verifyCommentsOnly({ baselineText, editedText, dialect }).outcome;
}

const PASS = VerifyOutcome.COMMENTS_ONLY;
const FAIL = VerifyOutcome.CODE_CHANGED;

test('removing whole-line, trailing and doc-free comments passes (C#)', () => {
  const baseline = 'class A\n{\n    // Set the id\n    int id = 1; // the id\n    /* block */\n    int b;\n}\n';
  const edited = 'class A\n{\n    int id = 1;\n    int b;\n}\n';
  assert.equal(outcome(baseline, edited, Dialect.CSHARP), PASS);
});

test('shortening a comment passes; changing a token next to it fails', () => {
  const baseline = 'int a = 1; // a long, long explanation\n';
  assert.equal(outcome(baseline, 'int a = 1; // short\n', Dialect.CSHARP), PASS);
  assert.equal(outcome(baseline, 'int a = 2; // short\n', Dialect.CSHARP), FAIL);
});

test('changing // text inside a string fails: it is code, not a comment', () => {
  const baseline = 'var u = "http://a"; // c\n';
  assert.equal(outcome(baseline, 'var u = "http://b"; // c\n', Dialect.CSHARP), FAIL);
  assert.equal(outcome('const u = `x // ${y}`;', 'const u = `x // ${z}`;', Dialect.SCRIPT), FAIL);
  assert.equal(outcome("SELECT 'a--b' -- c", "SELECT 'a--x' -- c", Dialect.SQL), FAIL);
});

test('whitespace inside a string is significant; whitespace between tokens on a line is not', () => {
  assert.equal(outcome('var s = "a  b";', 'var s = "a b";', Dialect.CSHARP), FAIL);
  assert.equal(outcome('int x = a  +  b;', 'int x = a + b;', Dialect.CSHARP), PASS);
});

test('joining lines fails (automatic semicolon insertion), including through a multi-line comment', () => {
  assert.equal(outcome('return\nx;', 'return x;', Dialect.SCRIPT), FAIL);
  assert.equal(outcome('return /*\n*/ x;', 'return x;', Dialect.SCRIPT), FAIL);
  assert.equal(outcome('return /*\n*/ x;', 'return\nx;', Dialect.SCRIPT), PASS);
});

test('deleting a comment that separated two tokens fails', () => {
  assert.equal(outcome('a/**/b', 'ab', Dialect.SCRIPT), FAIL);
  assert.equal(outcome('a/**/b', 'a b', Dialect.SCRIPT), PASS);
});

test('html and sql comment removal passes; an attribute change fails', () => {
  assert.equal(outcome('<div>\n  <!-- why -->\n  <p>x</p>\n</div>', '<div>\n  <p>x</p>\n</div>', Dialect.HTML), PASS);
  assert.equal(outcome('<p title="a">x</p>', '<p title="b">x</p>', Dialect.HTML), FAIL);
  assert.equal(outcome('-- note\nSELECT 1 /* x */ FROM t', 'SELECT 1 FROM t', Dialect.SQL), PASS);
});

test('a failure names the line of the first difference in each file', () => {
  const baseline = '// header\nint a = 1;\nint b = 2;\n';
  const edited = 'int a = 1;\nint b = 3;\n';
  const result = verifyCommentsOnly({ baselineText: baseline, editedText: edited, dialect: Dialect.CSHARP });
  assert.equal(result.outcome, FAIL);
  assert.equal(result.baseline.line, 3);
  assert.equal(result.edited.line, 2);
  assert.match(result.edited.context, /int b = 3/);
});

test('firstDifference finds the first mismatch, a length difference, or none', () => {
  assert.equal(firstDifference(['a', 'b'], ['a', 'b']), -1);
  assert.equal(firstDifference(['a', 'b'], ['a', 'c']), 1);
  assert.equal(firstDifference(['a'], ['a', 'b']), 1);
});
