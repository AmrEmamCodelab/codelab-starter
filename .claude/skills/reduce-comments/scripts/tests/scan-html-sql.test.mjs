import assert from 'node:assert/strict';
import { test } from 'node:test';
import { scanHtml } from '../lib/scan-html.mjs';
import { scanSql } from '../lib/scan-sql.mjs';
import { assertTiles, commentsOf, stringsOf } from './helpers.mjs';

function scanTemplate(text) {
  const segments = scanHtml(text);
  assertTiles(assert, text, segments);
  return segments;
}

function scanQuery(text) {
  const segments = scanSql(text);
  assertTiles(assert, text, segments);
  return segments;
}

test('html: <!-- inside an attribute value or a binding is not a comment', () => {
  const text = '<!-- real --><div title="<!-- not -->" [x]="a // b">x</div>';
  const segments = scanTemplate(text);
  assert.deepEqual(commentsOf(text, segments), ['<!-- real -->']);
  assert.deepEqual(stringsOf(text, segments), ['"<!-- not -->"', '"a // b"']);
});

test('html: quoted strings in an interpolation are literals', () => {
  const text = "<p>{{ '<!-- x -->' | t }}</p><!-- c -->";
  const segments = scanTemplate(text);
  assert.deepEqual(commentsOf(text, segments), ['<!-- c -->']);
  assert.deepEqual(stringsOf(text, segments), ["'<!-- x -->'"]);
});

test('html: a script body is raw text, and an apostrophe in text opens nothing', () => {
  const text = "<script>var a = '<!--';</script><p>Don't <!-- c --></p>";
  const segments = scanTemplate(text);
  assert.deepEqual(commentsOf(text, segments), ['<!-- c -->']);
});

test('sql: -- inside a string, an N string, a quoted or bracketed identifier is not a comment', () => {
  const text = "SELECT 'a--b', N'x--y', \"col--x\", [we--ird], [a]]--b] -- c";
  const segments = scanQuery(text);
  assert.deepEqual(commentsOf(text, segments), ['-- c']);
  assert.deepEqual(stringsOf(text, segments), ["'a--b'", "'x--y'", '"col--x"', '[we--ird]', '[a]]--b]']);
});

test('sql: block comments nest, doubled quotes escape, and // is not a comment', () => {
  const text = "/* a /* b */ c */ SELECT 'it''s -- fine', 1 // 2";
  const segments = scanQuery(text);
  assert.deepEqual(commentsOf(text, segments), ['/* a /* b */ c */']);
  assert.deepEqual(stringsOf(text, segments), ["'it''s -- fine'"]);
});
