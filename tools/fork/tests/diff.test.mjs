// Unit tests of the git diff parser and rewriter. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { isBinaryPatch, parseDiff, parseHeaderPath, quotePath, renamePatch, serializeDiff, unquotePath, withHunksOf } from '../lib/diff.mjs';
import { createRenamer } from '../lib/rename.mjs';

const OLD = 'a'.repeat(40);
const NEW = 'b'.repeat(40);
const ZERO = '0'.repeat(40);

const MODIFY = [
  'diff --git a/backend/src/Starter.Api/Program.cs b/backend/src/Starter.Api/Program.cs',
  `index ${OLD}..${NEW} 100644`,
  '--- a/backend/src/Starter.Api/Program.cs',
  '+++ b/backend/src/Starter.Api/Program.cs',
  '@@ -1,3 +1,4 @@ namespace Starter.Api;',
  ' namespace Starter.Api;',
  '-const string Db = "StarterDev";\r',
  '+const string Db = "StarterDev2";\r',
  '+// Restarter',
  ' }',
  '\\ No newline at end of file',
].join('\n');

const ADD = [
  'diff --git a/docs/new.md b/docs/new.md',
  'new file mode 100644',
  `index ${ZERO}..${NEW}`,
  '--- /dev/null',
  '+++ b/docs/new.md',
  '@@ -0,0 +1 @@',
  '+# Starter',
].join('\n');

const DELETE = [
  'diff --git a/old.txt b/old.txt',
  'deleted file mode 100644',
  `index ${OLD}..${ZERO}`,
  '--- a/old.txt',
  '+++ /dev/null',
  '@@ -1,2 +0,0 @@',
  '-one',
  '-two',
].join('\n');

const BINARY = [
  'diff --git a/assets/Starter.png b/assets/Starter.png',
  `index ${OLD}..${NEW}`,
  'GIT binary patch',
  'literal 5',
  'McmZQzfB*mh',
  '',
  'literal 0',
  'HcmV?d00001',
  '',
].join('\n');

const FULL = `${MODIFY}\n${ADD}\n${DELETE}\n${BINARY}\n`;

describe('parseDiff', () => {
  const patches = parseDiff(FULL);

  it('splits one patch per file with paths, kinds and blob ids', () => {
    assert.deepEqual(patches.map((patch) => patch.path), ['backend/src/Starter.Api/Program.cs', 'docs/new.md', 'old.txt', 'assets/Starter.png']);
    assert.deepEqual(patches.map((patch) => [patch.isNew, patch.isDeleted]), [[false, false], [true, false], [false, true], [false, false]]);
    assert.equal(patches[0].oldBlob, OLD);
    assert.equal(patches[0].newBlob, NEW);
    assert.equal(isBinaryPatch(patches[3]), true);
  });

  it('reads hunk headers, defaulting an omitted count to 1', () => {
    const [hunk] = patches[1].hunks;
    assert.deepEqual([hunk.oldStart, hunk.oldCount, hunk.newStart, hunk.newCount], [0, 0, 1, 1]);
    assert.equal(patches[0].hunks[0].section, ' namespace Starter.Api;');
    assert.equal(patches[0].hunks[0].lines.length, 6);
  });

  it('round-trips through serializeDiff (counts made explicit)', () => {
    assert.equal(serializeDiff(patches), FULL.replace('@@ -0,0 +1 @@', '@@ -0,0 +1,1 @@'));
  });

  it('refuses text before the first file header and malformed hunk headers', () => {
    assert.throws(() => parseDiff('garbage\n'), /before the first/);
    assert.throws(() => parseDiff('diff --git a/x b/x\n@@ nonsense\n'), /Malformed hunk header/);
  });

  it('returns no patches for an empty diff', () => {
    assert.deepEqual(parseDiff(''), []);
  });
});

describe('paths', () => {
  it('reads plain and C-quoted header paths, including paths with spaces', () => {
    assert.deepEqual(parseHeaderPath('diff --git a/a b/c.txt b/a b/c.txt'), { path: 'a b/c.txt', quoted: false });
    assert.deepEqual(parseHeaderPath('diff --git "a/tab\\there" "b/tab\\there"'), { path: 'tab\there', quoted: true });
  });

  it('decodes octal UTF-8 escapes and re-quotes control characters', () => {
    assert.equal(unquotePath('"a/\\331\\202"'), 'a/ق');
    assert.equal(quotePath('a/x"y\\z\t'), '"a/x\\"y\\\\z\\t"');
  });
});

describe('renamePatch', () => {
  const renamer = createRenamer([{ from: 'Starter', to: 'Acme' }]);
  const [modify, add, , binary] = parseDiff(FULL);

  it('renames the path, every hunk line and the section, never the markers or counts', () => {
    const renamed = renamePatch(modify, renamer);
    assert.equal(renamed.path, 'backend/src/Acme.Api/Program.cs');
    assert.equal(renamed.hunks[0].section, ' namespace Acme.Api;');
    assert.deepEqual(renamed.hunks[0].lines, [' namespace Acme.Api;', '-const string Db = "AcmeDev";\r', '+const string Db = "AcmeDev2";\r', '+// Restarter', ' }', '\\ No newline at end of file']);
    assert.equal(renamed.hunks[0].oldCount, modify.hunks[0].oldCount);
    const text = serializeDiff([renamed]);
    assert.match(text, /^diff --git a\/backend\/src\/Acme\.Api\/Program\.cs b\/backend\/src\/Acme\.Api\/Program\.cs\n/);
    assert.match(text, /\n--- a\/backend\/src\/Acme\.Api\/Program\.cs\n\+\+\+ b\/backend\/src\/Acme\.Api\/Program\.cs\n/);
  });

  it('keeps /dev/null sides and renames added content', () => {
    const text = serializeDiff([renamePatch(add, renamer)]);
    assert.match(text, /--- \/dev\/null\n\+\+\+ b\/docs\/new\.md\n/);
    assert.match(text, /\+# Acme$/m);
  });

  it('renames only the path of a binary patch', () => {
    const renamed = renamePatch(binary, renamer);
    assert.equal(renamed.path, 'assets/Acme.png');
    assert.deepEqual(renamed.binary, binary.binary);
  });

  it('writes rewritten blob ids into the index line, keeping the mode', () => {
    const text = serializeDiff([{ ...modify, oldBlob: 'c'.repeat(40) }]);
    assert.match(text, new RegExp(`index ${'c'.repeat(40)}\\.\\.${NEW} 100644`));
  });

  it('withHunksOf takes the hunks of a regenerated diff', () => {
    const replaced = withHunksOf(modify, add);
    assert.equal(replaced.path, modify.path);
    assert.deepEqual(replaced.hunks, add.hunks);
  });
});
