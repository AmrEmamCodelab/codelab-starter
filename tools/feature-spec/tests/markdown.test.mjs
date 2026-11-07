import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { contentLines, firstTable, orderedListItems, parseFrontMatter, parseSections, splitLines, splitRow } from '../lib/markdown.mjs';

describe('parseFrontMatter', () => {
  it('reads scalars, quoted scalars, empty values and block lists', () => {
    const lines = splitLines('---\nfeature: a-b\nstatus: "draft"\napprovedBy:\ninputs:\n  - one\n  - \'two\'\n# comment\n---\nbody');
    const result = parseFrontMatter(lines);
    assert.equal(result.found, true);
    assert.deepEqual(result.errors, []);
    assert.equal(result.values.get('feature'), 'a-b');
    assert.equal(result.values.get('status'), 'draft');
    assert.deepEqual(result.values.get('approvedBy'), []);
    assert.deepEqual(result.values.get('inputs'), ['one', 'two']);
    assert.equal(lines[result.bodyStart], 'body');
  });

  it('reads flow lists', () => {
    const result = parseFrontMatter(splitLines('---\ninputs: [a, "b c"]\nempty: []\n---'));
    assert.deepEqual(result.values.get('inputs'), ['a', 'b c']);
    assert.deepEqual(result.values.get('empty'), []);
  });

  it('is not found without an opening or a closing fence', () => {
    assert.equal(parseFrontMatter(splitLines('# Title\n')).found, false);
    assert.equal(parseFrontMatter(splitLines('---\nfeature: x\n')).found, false);
  });

  it('reports lines it cannot read with their 1-based line number', () => {
    const result = parseFrontMatter(splitLines('---\nfeature: x\n: nothing\n---'));
    assert.deepEqual(result.errors.map((error) => error.line), [3]);
  });
});

describe('sections, tables and lists', () => {
  const text = [
    '# Title',
    '## One',
    '',
    '| A | B |',
    '| --- | :---: |',
    '| 1 | 2 \\| 3 |',
    '',
    'After.',
    '## Two',
    '1. first',
    '2) second',
    '```',
    '3. fenced',
    '| X | Y |',
    '```',
  ].join('\n');
  const sections = parseSections(splitLines(text), 0);

  it('splits on level-2 headings with 1-based line numbers', () => {
    assert.deepEqual(sections.map((section) => [section.name, section.line]), [['One', 2], ['Two', 9]]);
  });

  it('reads the first table with its rows', () => {
    const table = firstTable(sections[0]);
    assert.deepEqual(table.header, ['A', 'B']);
    assert.deepEqual(table.rows.map((row) => row.cells), [['1', '2 | 3']]);
    assert.equal(table.rows[0].line, 6);
  });

  it('needs a separator row to call it a table, and ignores fenced tables', () => {
    assert.equal(firstTable({ lines: [{ text: '| A |', line: 1 }, { text: '| 1 |', line: 2 }] }), null);
    assert.equal(firstTable(sections[1]), null);
  });

  it('reads ordered-list items outside code fences', () => {
    assert.deepEqual(orderedListItems(sections[1]).map((item) => item.text), ['first', 'second']);
  });

  it('splits a row without outer pipes', () => {
    assert.deepEqual(splitRow('a | b'), ['a', 'b']);
  });

  it('drops blank and commented lines from the content', () => {
    const section = { lines: [{ text: '' }, { text: '<!-- a' }, { text: 'b -->' }, { text: 'kept <!-- x --> too' }] };
    assert.deepEqual(contentLines(section).map((entry) => entry.text), ['kept  too']);
  });
});
