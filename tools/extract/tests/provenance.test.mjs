import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { gitBlobId } from '../lib/hashing.mjs';
import {
  classifyLineEndingMismatches, compareHarvest, groupByDirectory, isFullMatch, parseLsTree, renderSourceMarkdown,
} from '../lib/provenance.mjs';

const NUL = String.fromCharCode(0);
const TAB = String.fromCharCode(9);
const BLOB = ['100644', 'blob'];
const SUBMODULE = ['160000', 'commit'];

/** Builds `git ls-tree -r -z` output from [mode, type, sha, path] rows. */
const lsTree = (rows) => rows.map(([mode, type, sha, path]) => `${mode} ${type} ${sha}${TAB}${path}${NUL}`).join('');
const SHA_A = 'a'.repeat(40);
const SHA_B = 'b'.repeat(40);
const SHA_C = 'c'.repeat(40);
const source = {
  product: 'SourceProduct', repository: 'X:/repo', branch: 'main', commit: SHA_A, treeHash: SHA_B, harvestRoot: 'X:/harvest',
};
const hardDeny = { caseInsensitive: ['*secret*'], caseSensitive: ['*Foo*'] };

test('gitBlobId equals git hash-object for the same bytes', () => {
  const bytes = Buffer.from('line one\r\nline two\n');
  const fromGit = spawnSync('git', ['hash-object', '--no-filters', '--stdin'], { input: bytes }).stdout.toString().trim();
  assert.equal(gitBlobId(bytes), fromGit);
});

test('ls-tree -z output is parsed, including paths with spaces', () => {
  const output = lsTree([[...BLOB, SHA_A, 'dir/a file.md'], [...SUBMODULE, SHA_B, 'sub']]);
  assert.deepEqual(parseLsTree(output), [
    { mode: '100644', type: 'blob', sha: SHA_A, path: 'dir/a file.md' },
    { mode: '160000', type: 'commit', sha: SHA_B, path: 'sub' },
  ]);
});

test('comparison classifies match, mismatch, missing, extra and non-blob entries', () => {
  const tree = parseLsTree(lsTree([
    [...BLOB, SHA_A, 'same'], [...BLOB, SHA_A, 'changed'], [...BLOB, SHA_A, 'gone'], [...SUBMODULE, SHA_B, 'sub'],
  ]));
  const harvest = new Map([['same', SHA_A], ['changed', SHA_C], ['added', SHA_A]]);
  const comparison = compareHarvest(tree, harvest);
  assert.equal(comparison.total, 3);
  assert.equal(comparison.matched, 1);
  assert.deepEqual(comparison.mismatched, [{ path: 'changed', expected: SHA_A, actual: SHA_C }]);
  assert.deepEqual(comparison.missing, ['gone']);
  assert.deepEqual(comparison.extra, ['added']);
  assert.deepEqual(comparison.nonBlob, ['sub']);
  assert.equal(isFullMatch(comparison), false);
});

test('a CRLF copy of an LF blob is classified as a line-ending-only mismatch', () => {
  const lf = Buffer.from('a\nb\n');
  const crlf = Buffer.from('a\r\nb\r\n');
  const mismatched = [{ path: 'f', expected: gitBlobId(lf), actual: gitBlobId(crlf) }];
  const normalized = () => gitBlobId(Buffer.from(crlf.toString().replace(/\r\n/g, '\n')));
  assert.equal(classifyLineEndingMismatches(mismatched, normalized)[0].lineEndingsOnly, true);
  assert.equal(classifyLineEndingMismatches(mismatched, () => SHA_C)[0].lineEndingsOnly, false);
});

test('paths are grouped by their two leading folders', () => {
  assert.deepEqual(groupByDirectory(['s/a/x', 's/a/y', 's/b/z']), [['s/a', 2], ['s/b', 1]]);
});

test('SOURCE.md shows PASS, the tree hash verdict, origins and the deny list', () => {
  const comparison = compareHarvest(parseLsTree(lsTree([[...BLOB, SHA_A, 'f']])), new Map([['f', SHA_A]]));
  const origins = [{ target: 't/f', source: 'f', batch: 'p0', entryId: 'e', mode: 'copy', sourceGitBlob: SHA_A }];
  const markdown = renderSourceMarkdown({
    source, resolvedCommit: SHA_A, resolvedTree: SHA_B, generatedOn: '2026-01-01', comparison, origins, hardDeny,
  });
  assert.match(markdown, /\*\*PASS: 100% match\.\*\* All 1 files/);
  assert.match(markdown, new RegExp(`${SHA_B}\` \\(matches the manifest\\)`));
  assert.match(markdown, /\| `t\/f` \| `f` \| p0 \/ e \| copy \|/);
  assert.match(markdown, /- `\*Foo\* \(case-sensitive\)`/);
});

test('SOURCE.md shows FAIL with the line-ending explanation when copies differ', () => {
  const tree = parseLsTree(lsTree([[...BLOB, SHA_A, 'svc/x/f']]));
  const strict = compareHarvest(tree, new Map([['svc/x/f', SHA_C]]));
  const comparison = { ...strict, mismatched: classifyLineEndingMismatches(strict.mismatched, () => SHA_A) };
  const markdown = renderSourceMarkdown({
    source, resolvedCommit: SHA_A, resolvedTree: SHA_C, generatedOn: '2026-01-01', comparison, origins: [], hardDeny,
  });
  assert.match(markdown, /\*\*FAIL \(byte-exact\)\.\*\* 0 of 1 files match byte for byte; 1 differ only in line endings/);
  assert.match(markdown, /\| `svc\/x` \| 1 \|/);
  assert.match(markdown, /DIFFERS from the manifest/);
  assert.match(markdown, /No harvested file has been promoted/);
});
