import assert from 'node:assert/strict';
import { test } from 'node:test';
import { matchesGlob } from '../lib/glob.mjs';
import { findDenyingPattern, loadManifest, MODE_COPY, resolveBatch, staticPrefix } from '../lib/manifest.mjs';
import { MANIFEST_FILE, samples } from './helpers.mjs';

const manifest = loadManifest(MANIFEST_FILE);

test('glob: "**" spans folders, "*" stays in a segment, braces alternate, bare names match at any depth', () => {
  assert.ok(matchesGlob('a/b/c.md', 'a/**'));
  assert.ok(matchesGlob('a/c.md', 'a/**/c.md'));
  assert.ok(!matchesGlob('a/b/c.md', 'a/*.md'));
  assert.ok(matchesGlob('x/key.pem', '*.{pfx,pem}'));
  assert.ok(matchesGlob('deep/dir/file.txt', 'file.txt'));
  assert.ok(!matchesGlob('deep/dir/file.txt', 'file.txt', { anchored: true }));
  assert.ok(matchesGlob('A/B.MD', 'a/*.md', { caseInsensitive: true }));
});

test('hard-deny catches every listed source-product path', () => {
  for (const path of samples.hardDeny.denied) {
    assert.ok(findDenyingPattern(manifest.hardDeny, path), `expected ${path} to be denied`);
  }
});

test('hard-deny does not catch look-alike names', () => {
  for (const path of samples.hardDeny.allowed) {
    assert.equal(findDenyingPattern(manifest.hardDeny, path), null, `expected ${path} to be allowed`);
  }
});

test('a batch copies allowlisted files, keeps hard-denied ones out, and maps targets', () => {
  const testManifest = {
    ...manifest,
    batches: {
      t: {
        entries: [
          { id: 'skills', source: 'skills/**', target: 'out/skills/', mode: MODE_COPY },
          { id: 'root-doc', source: 'GUIDE.md', target: 'ref/GUIDE.md', mode: MODE_COPY },
          { id: 'stale', source: 'gone/**', target: 'x/', mode: MODE_COPY },
        ],
      },
    },
  };
  const deniedSample = samples.hardDeny.denied.find((path) => path.endsWith('.pem'));
  const files = ['skills/a/SKILL.md', `skills/${deniedSample.split('/').pop()}`, 'GUIDE.md', 'nested/GUIDE.md'];
  const { copies, denied, emptyEntries } = resolveBatch(testManifest, 't', files);
  assert.deepEqual(copies.map((copy) => [copy.source, copy.target]), [
    ['skills/a/SKILL.md', 'out/skills/a/SKILL.md'],
    ['GUIDE.md', 'ref/GUIDE.md'],
  ]);
  assert.equal(denied.length, 1);
  assert.deepEqual(emptyEntries, ['stale']);
});

test('an unknown batch is an error that names the known batches', () => {
  assert.throws(() => resolveBatch(manifest, 'nope', []), /unknown batch "nope" \(known: p0/);
});

test('static prefix of a glob', () => {
  assert.equal(staticPrefix('a/b/**'), 'a/b/');
  assert.equal(staticPrefix('**/x'), '');
  assert.equal(staticPrefix('a/file.md'), 'a/file.md');
});

test('the committed manifest pins the source commit and declares the P0 batch', () => {
  assert.match(manifest.source.commit, /^[0-9a-f]{40}$/);
  assert.match(manifest.source.treeHash, /^[0-9a-f]{40}$/);
  const ids = manifest.batches.p0.entries.map((entry) => entry.id);
  for (const expected of ['skill-coding-standards', 'skill-do-work', 'skill-write-a-skill', 'release-stamping-reference',
    'claude-md-structure', 'context-md-structure']) {
    assert.ok(ids.includes(expected), expected);
  }
});
