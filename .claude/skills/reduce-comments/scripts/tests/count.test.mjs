import assert from 'node:assert/strict';
import { test } from 'node:test';
import { countComments } from '../lib/count.mjs';
import { Dialect, scan } from '../lib/dialects.mjs';

function count(text, dialect = Dialect.CSHARP) {
  return countComments(text, scan(text, dialect));
}

test('a run of whole-line comments is one unit; doc and plain runs are separate units', () => {
  const text = '/// <summary>\n/// Doc.\n/// </summary>\n// one\n// two\nint a;\n';
  assert.deepEqual(count(text), { units: 2, docUnits: 1, commentLines: 5, fileLines: 6 });
});

test('a trailing comment and a block comment are one unit each, and a gap ends a run', () => {
  const text = 'int a; // trailing\n// next line\n\n// after gap\n/* block\n   two lines */\n';
  assert.deepEqual(count(text), { units: 4, docUnits: 0, commentLines: 5, fileLines: 6 });
});

test('a file without comments counts zero and a missing final newline still counts the last line', () => {
  assert.deepEqual(count('int a;\nint b;'), { units: 0, docUnits: 0, commentLines: 0, fileLines: 2 });
  assert.deepEqual(count(''), { units: 0, docUnits: 0, commentLines: 0, fileLines: 0 });
});

test('sql and html comments are counted in their own syntax', () => {
  assert.equal(count('-- a\n-- b\nSELECT 1; /* c */', Dialect.SQL).units, 2);
  assert.equal(count('<!-- a -->\n<p></p>\n<!-- b -->', Dialect.HTML).units, 2);
});
