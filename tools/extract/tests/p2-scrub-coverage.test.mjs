// Phase 2 exit criterion (ScrubSweep): the sweep reaches every new phase 2 root (backend/, frontend/, the owned DGA
// package, init.ps1, run-local.ps1, codelab.starter.json, tools/fork, tools/scaffold) and every file type under them
// (scss, html, json, yaml, md, ps1, mjs), including untracked files inside a git work tree. The samples live in
// tests/fixtures/scrub-samples.json (p2Roots), the only place that may spell the terms.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { loadScrubConfiguration, scrubFile, scrubTree } from '../lib/scrubber.mjs';
import { EXCEPTIONS_FILE, makeTempDirectory, REPO_ROOT, samples, TERMS_FILE, writeTree } from './helpers.mjs';

const configuration = loadScrubConfiguration({ termsFile: TERMS_FILE, exceptionsFile: EXCEPTIONS_FILE });
const GIT_NOT_IGNORED = 1;
const LINE_SEPARATOR = '\n';

/** The phase 2 samples grouped as one file per path (several samples may share a path). */
function treeOfSamples() {
  const tree = {};
  for (const { path, line } of samples.p2Roots) tree[path] = `${tree[path] ?? ''}${line}${LINE_SEPARATOR}`;
  return tree;
}

function git(cwd, args) {
  return spawnSync('git', args, { cwd, encoding: 'utf8', windowsHide: true });
}

function assertEverySampleReported(findings) {
  for (const { path, line, rule } of samples.p2Roots) {
    const hit = findings.some((finding) => finding.path === path && finding.ruleId === rule);
    assert.ok(hit, `expected ${rule} in ${path} for: ${line}`);
  }
}

test('every phase 2 sample is flagged under its rule at its own path', () => {
  for (const { path, line, rule } of samples.p2Roots) {
    const ruleIds = scrubFile(path, line, configuration).map((finding) => finding.ruleId);
    assert.ok(ruleIds.includes(rule), `expected ${rule} in ${path} for: ${line}`);
  }
});

test('the phase 2 samples cover every new root named by the exit criterion', () => {
  const roots = ['backend/', 'frontend/starter-web/', 'packages/npm/ng-dga/', 'init.ps1', 'run-local.ps1',
    'codelab.starter.json', 'tools/fork/', 'tools/scaffold/', 'rules/', '.claude/', 'docs/', 'CLAUDE.md'];
  const extensions = ['.scss', '.html', '.json', '.yaml', '.md', '.ps1', '.mjs', '.csproj'];
  for (const root of roots) {
    assert.ok(samples.p2Roots.some(({ path }) => path.startsWith(root)), `no sample under ${root}`);
  }
  for (const extension of extensions) {
    assert.ok(samples.p2Roots.some(({ path }) => path.endsWith(extension)), `no ${extension} sample`);
  }
});

test('a staging-style tree (no git) reports every phase 2 sample', (testContext) => {
  const root = makeTempDirectory(testContext);
  writeTree(root, treeOfSamples());
  assertEverySampleReported(scrubTree(root, configuration).findings);
});

test('inside a git work tree, untracked files under the phase 2 roots are scanned', (testContext) => {
  const root = makeTempDirectory(testContext);
  assert.equal(git(root, ['init', '--quiet']).status, 0, 'git init');
  writeTree(root, treeOfSamples());
  assertEverySampleReported(scrubTree(root, configuration).findings);
});

test('the repository .gitignore does not hide any phase 2 root from the sweep', () => {
  for (const { path } of samples.p2Roots) {
    const result = git(REPO_ROOT, ['check-ignore', '--quiet', '--no-index', path]);
    assert.equal(result.status, GIT_NOT_IGNORED, `${path} is ignored by git, so the sweep would never read it`);
  }
});
