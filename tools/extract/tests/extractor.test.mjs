import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { stringify } from 'yaml';
import { EXIT_CLEAN, EXIT_FINDINGS, EXIT_USAGE_OR_ERROR } from '../lib/exit-codes.mjs';
import { headingsOnly, stageCopies } from '../lib/extractor.mjs';
import { contentDigest, gitBlobId } from '../lib/hashing.mjs';
import { loadManifest, MODE_COPY, MODE_HEADINGS_ONLY } from '../lib/manifest.mjs';
import { makeTempDirectory, MANIFEST_FILE, runTool, samples, writeTree } from './helpers.mjs';

test('headings-only keeps headings outside code fences and drops every body line', () => {
  const markdown = ['# Title', 'body text', '```', '# not a heading', '```', '## Section', 'more'].join('\n');
  const result = headingsOnly(markdown);
  assert.match(result, /# Title\n\n## Section\n$/);
  assert.ok(!result.includes('body text'));
  assert.ok(!result.includes('not a heading'));
});

test('staging writes each copy and records origin hashes of the SOURCE bytes', (testContext) => {
  const harvest = makeTempDirectory(testContext);
  const staging = makeTempDirectory(testContext);
  writeTree(harvest, { 'docs/A.md': '# A\nbody\n', 'src/b.txt': 'bytes' });
  const origins = stageCopies({
    batchName: 'b1',
    harvestRoot: harvest,
    stagingFiles: staging,
    copies: [
      { entryId: 'doc', source: 'docs/A.md', target: 'ref/A.md', mode: MODE_HEADINGS_ONLY },
      { entryId: 'src', source: 'src/b.txt', target: 'src/b.txt', mode: MODE_COPY },
    ],
  });
  assert.equal(readFileSync(join(staging, 'src/b.txt'), 'utf8'), 'bytes');
  assert.ok(!readFileSync(join(staging, 'ref/A.md'), 'utf8').includes('unique-body-line'));
  assert.equal(origins[1].sourceGitBlob, gitBlobId(Buffer.from('bytes')));
  assert.equal(origins[1].sourceSha256, origins[1].stagedSha256);
  assert.notEqual(origins[0].sourceSha256, origins[0].stagedSha256);
});

test('extract-core stages a batch, reports hard-denied files and refuses a non-empty staging folder', (testContext) => {
  const root = makeTempDirectory(testContext);
  const harvest = join(root, 'harvest');
  const staging = join(root, 'staging');
  const deniedName = samples.hardDeny.denied.find((path) => path.endsWith('.pem')).split('/').pop();
  writeTree(harvest, { 'skills/s/SKILL.md': '# Skill\n', [`skills/s/${deniedName}`]: 'x' });
  const realManifest = loadManifest(MANIFEST_FILE);
  const testManifest = {
    ...realManifest,
    source: { ...realManifest.source, harvestRoot: harvest },
    batches: { t: { entries: [{ id: 'skills', source: 'skills/**', target: 'out/', mode: MODE_COPY }] } },
  };
  writeTree(root, { 'manifest.yaml': stringify(testManifest) });
  const args = ['--batch', 't', '--staging', staging, '--manifest', join(root, 'manifest.yaml')];

  const first = runTool('extract-core.mjs', args);
  assert.equal(first.status, EXIT_CLEAN, first.stdout + first.stderr);
  assert.match(first.stdout, /1 file\(s\) staged, 1 hard-denied/);
  const document = JSON.parse(readFileSync(join(staging, 'origins.json'), 'utf8'));
  assert.deepEqual(document.origins.map((origin) => origin.target), ['out/s/SKILL.md']);
  assert.equal(readFileSync(join(staging, 'files', 'out', 's', 'SKILL.md'), 'utf8'), '# Skill\n');

  const second = runTool('extract-core.mjs', args);
  assert.equal(second.status, EXIT_USAGE_OR_ERROR);
  assert.match(second.stderr, /is not empty/);
});

test('extract-core exits 1 when an allowlist entry matches nothing (stale manifest)', (testContext) => {
  const root = makeTempDirectory(testContext);
  writeTree(root, { 'harvest/keep.md': '# k\n' });
  const realManifest = loadManifest(MANIFEST_FILE);
  const testManifest = {
    ...realManifest,
    source: { ...realManifest.source, harvestRoot: join(root, 'harvest') },
    batches: { t: { entries: [{ id: 'missing', source: 'nothing/**', target: 'x/', mode: MODE_COPY }] } },
  };
  writeTree(root, { 'manifest.yaml': stringify(testManifest) });
  const result = runTool('extract-core.mjs', ['--batch', 't', '--staging', join(root, 'staging'), '--manifest', join(root, 'manifest.yaml')]);
  assert.equal(result.status, EXIT_FINDINGS);
  assert.match(result.stdout, /EMPTY {2}allowlist entry "missing"/);
});

test('regression: a batch with its own source (the package spike) is extracted only while it matches its contentDigest', (testContext) => {
  const root = makeTempDirectory(testContext);
  const spike = join(root, 'spike');
  writeTree(spike, { 'src/A.cs': 'class A { }\n', 'sample/B.cs': 'class B { }\n' });
  const digest = contentDigest([
    { path: 'src/A.cs', bytes: Buffer.from('class A { }\n') },
    { path: 'sample/B.cs', bytes: Buffer.from('class B { }\n') },
  ]);
  const realManifest = loadManifest(MANIFEST_FILE);
  const manifestWith = (contentDigestValue) => stringify({
    ...realManifest,
    batches: {
      spike: {
        source: { description: 'test spike', harvestRoot: spike, contentDigest: contentDigestValue },
        entries: [{ id: 'src', source: 'src/**', target: 'ref/src/', mode: MODE_COPY }],
      },
    },
  });
  writeTree(root, { 'good.yaml': manifestWith(digest), 'stale.yaml': manifestWith('0'.repeat(64)) });

  const good = runTool('extract-core.mjs', ['--batch', 'spike', '--staging', join(root, 'staging-good'), '--manifest', join(root, 'good.yaml')]);
  assert.equal(good.status, EXIT_CLEAN, good.stdout + good.stderr);
  const document = JSON.parse(readFileSync(join(root, 'staging-good', 'origins.json'), 'utf8'));
  assert.equal(document.sourceContentDigest, digest);
  assert.equal(document.sourceCommit, null);
  assert.deepEqual(document.origins.map((origin) => origin.target), ['ref/src/A.cs']);

  const stale = runTool('extract-core.mjs', ['--batch', 'spike', '--staging', join(root, 'staging-stale'), '--manifest', join(root, 'stale.yaml')]);
  assert.notEqual(stale.status, EXIT_CLEAN);
  assert.match(stale.stderr + stale.stdout, /does not match source\.contentDigest/);
});

test('contentDigest does not depend on file order', () => {
  const files = [{ path: 'b', bytes: Buffer.from('2') }, { path: 'a', bytes: Buffer.from('1') }];
  assert.equal(contentDigest(files), contentDigest([...files].reverse()));
});
