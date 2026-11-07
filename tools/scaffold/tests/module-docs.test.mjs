import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { addRecipe, addRequiredRule, addTableRow, addUseNotes, eolOf, ruleIdsOf } from '../lib/module-docs.mjs';

const DOC = '# Demo\n\n## Purpose\n\nDemo.\n\n## Use\n\n- One.\n\n## Extend\n\nMore.\n';
const RULE = Object.freeze({ id: 'demo.flow', idSuffix: 'flow', statement: 'The flow lives in "one" place.', enforcedBy: ['review'] });

describe('module docs: MODULE.md notes', () => {
  it('appends notes at the end of Use, once', () => {
    const once = addUseNotes(DOC, ['- Two.']);
    assert.equal(once, '# Demo\n\n## Purpose\n\nDemo.\n\n## Use\n\n- One.\n- Two.\n\n## Extend\n\nMore.\n');
    assert.equal(addUseNotes(once, ['- Two.']), once);
  });

  it('puts notes before the marker when the file has one, and at the end without a Use section', () => {
    assert.equal(addUseNotes('## Use\n\n<!-- scaffold:use -->\nTail.\n', ['- A.']), '## Use\n\n- A.\n<!-- scaffold:use -->\nTail.\n');
    assert.equal(addUseNotes('# Demo\n\nText.\n', ['- A.']), '# Demo\n\nText.\n\n- A.\n');
    assert.equal(addUseNotes('## Use\n\n- One.\n', ['- A.']), '## Use\n\n- One.\n- A.\n');
  });

  it('keeps CRLF line endings', () => {
    const crlf = DOC.replaceAll('\n', '\r\n');
    assert.equal(eolOf(crlf), '\r\n');
    assert.ok(addUseNotes(crlf, ['- Two.']).includes('- One.\r\n- Two.\r\n\r\n## Extend'));
  });
});

describe('module docs: module.rules.yaml', () => {
  it('adds a recipe to the one-line list, once, or appends the list', () => {
    const yaml = 'module: Demo\nrecipes: [docs/recipes/add-endpoint.md]\n';
    const added = addRecipe(yaml, 'docs/recipes/add-lookup.md');
    assert.deepEqual(added, { text: 'module: Demo\nrecipes: [docs/recipes/add-endpoint.md, docs/recipes/add-lookup.md]\n', added: true });
    assert.deepEqual(addRecipe(added.text, 'docs/recipes/add-lookup.md'), { text: added.text, added: false });
    assert.equal(addRecipe('module: Demo\n', 'docs/recipes/add-lookup.md').text, 'module: Demo\nrecipes: [docs/recipes/add-lookup.md]\n');
    assert.equal(addRecipe('recipes: []\n', 'a.md').text, 'recipes: [a.md]\n');
  });

  it('appends a required rule at the end of the block, before comments that belong to the next key', () => {
    const yaml = 'required:\n  - id: demo.a\n    statement: A.\n    enforcedBy: [review]\n\n# forbidden\nforbidden: []\n';
    const { text, added } = addRequiredRule(yaml, RULE);
    assert.ok(added);
    assert.equal(text, 'required:\n  - id: demo.a\n    statement: A.\n    enforcedBy: [review]\n  - id: demo.flow\n    statement: "The flow lives in \\"one\\" place."\n    enforcedBy: [review]\n\n# forbidden\nforbidden: []\n');
    assert.deepEqual(ruleIdsOf(text), ['demo.a', 'demo.flow']);
    assert.equal(addRequiredRule(text, RULE).added, false);
  });

  it('skips a rule that exists under another prefix-qualified name, and creates the block when absent', () => {
    assert.equal(addRequiredRule('required:\n  - id: other.flow\n    statement: X.\n    enforcedBy: [review]\n', RULE).added, false);
    assert.equal(addRequiredRule('module: Demo\nrecipes: [a.md]\n', RULE).text,
      'module: Demo\nrequired:\n  - id: demo.flow\n    statement: "The flow lives in \\"one\\" place."\n    enforcedBy: [review]\nrecipes: [a.md]\n');
    assert.equal(addRequiredRule('module: Demo\n', RULE).text,
      'module: Demo\nrequired:\n  - id: demo.flow\n    statement: "The flow lives in \\"one\\" place."\n    enforcedBy: [review]\n');
  });
});

describe('module docs: Markdown tables', () => {
  const table = 'Seeded:\n\n| Type | Ids |\n| --- | --- |\n| `a` | `AIds` |\n\n## Use\n';

  it('adds a row after the last row, once', () => {
    const once = addTableRow(table, '| Type | Ids |', '| `b` | `BIds` |');
    assert.equal(once, 'Seeded:\n\n| Type | Ids |\n| --- | --- |\n| `a` | `AIds` |\n| `b` | `BIds` |\n\n## Use\n');
    assert.equal(addTableRow(once, '| Type | Ids |', '| `b` | `BIds` |'), once);
  });

  it('answers null when the table is missing', () => {
    assert.equal(addTableRow('# Nothing\n', '| Type | Ids |', '| x |'), null);
  });
});
