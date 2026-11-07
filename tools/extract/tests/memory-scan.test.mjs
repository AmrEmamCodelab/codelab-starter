// Phase 9 fix "memory with secrets": .claude/memory is gitignored (never shipped) but the secret scan still reads it,
// so a credential a session wrote into memory is reported instead of hiding behind .gitignore.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { ALWAYS_SECRET_SCANNED_DIRECTORIES, listFiles, listSecretScanFiles } from '../lib/files.mjs';
import { scanTreeForSecrets } from '../lib/secret-scanner.mjs';
import { joined, makeTempDirectory, REPO_ROOT, runTool, writeTree } from './helpers.mjs';

const NO_DIGESTS = new Map();
const MEMORY_DIRECTORY = '.claude/memory';
const MEMORY_FILE = `${MEMORY_DIRECTORY}/project-notes.md`;
const PEM_BEGIN = joined('-----BEGIN', ' RSA PRIVATE', ' KEY-----');
const PRIVATE_KEY_RULE = 'private-key';
const EXIT_FINDINGS = 1;
const GITIGNORE_LINE = /^\.claude\/memory\/$/m;

function gitInit(root) {
  execFileSync('git', ['init', '-q'], { cwd: root, stdio: 'ignore' });
}

function memoryRepository(testContext) {
  const root = makeTempDirectory(testContext);
  gitInit(root);
  writeTree(root, {
    '.gitignore': `${MEMORY_DIRECTORY}/\nbin/\n`,
    'README.md': 'starter notes\n',
    [MEMORY_FILE]: `notes from a session\n${PEM_BEGIN}\nAAAA\n`,
    'bin/build.log': `${PEM_BEGIN}\n`,
  });
  return root;
}

test('the repository gitignores .claude/memory/', () => {
  assert.match(readFileSync(join(REPO_ROOT, '.gitignore'), 'utf8'), GITIGNORE_LINE);
  assert.deepEqual([...ALWAYS_SECRET_SCANNED_DIRECTORIES], [MEMORY_DIRECTORY]);
});

test('regression: a secret in the gitignored .claude/memory is still found by the secret scan', (testContext) => {
  const root = memoryRepository(testContext);
  assert.ok(!listFiles(root).includes(MEMORY_FILE), 'listFiles honours .gitignore (scrub sweep and content check)');
  assert.ok(listSecretScanFiles(root).includes(MEMORY_FILE), 'the secret scan lists the memory file');
  const findings = scanTreeForSecrets(root, NO_DIGESTS).findings;
  assert.deepEqual(findings.filter((finding) => finding.path === MEMORY_FILE).map((finding) => finding.ruleId), [PRIVATE_KEY_RULE]);
});

test('other gitignored folders stay skipped by the secret scan', (testContext) => {
  const root = memoryRepository(testContext);
  assert.ok(!listSecretScanFiles(root).includes('bin/build.log'));
});

test('the secret-scan CLI fails on a secret in .claude/memory', (testContext) => {
  const root = memoryRepository(testContext);
  const result = runTool('secret-scan.mjs', ['--root', root]);
  assert.equal(result.status, EXIT_FINDINGS);
  assert.match(`${result.stdout}\n${result.stderr}`, /\.claude\/memory\/project-notes\.md/);
});

test('without a .claude/memory folder the file list is the git list', (testContext) => {
  const root = makeTempDirectory(testContext);
  gitInit(root);
  writeTree(root, { 'a.txt': 'a\n', 'b/c.txt': 'c\n' });
  assert.deepEqual(listSecretScanFiles(root), listFiles(root));
});
