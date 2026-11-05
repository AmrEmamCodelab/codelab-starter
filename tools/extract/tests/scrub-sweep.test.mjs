import assert from 'node:assert/strict';
import { test } from 'node:test';
import { join } from 'node:path';
import { loadScrubConfiguration, scrubFile, scrubTree } from '../lib/scrubber.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from '../lib/exit-codes.mjs';
import {
  EXCEPTIONS_FILE, makeTempDirectory, REPO_ROOT, runTool, samples, TERMS_FILE, writeTree,
} from './helpers.mjs';

const configuration = loadScrubConfiguration({ termsFile: TERMS_FILE, exceptionsFile: EXCEPTIONS_FILE });
const NEUTRAL_PATH = 'src/sample.txt';
const OWN_FOLDER = 'tools/extract/';
const ruleIdsFor = (path, text) => scrubFile(path, text, configuration).map((finding) => finding.ruleId);

test('every flagged sample is reported under its rule id', () => {
  for (const { rule, line } of samples.flagged) {
    assert.ok(ruleIdsFor(NEUTRAL_PATH, line).includes(rule), `expected ${rule} for: ${line}`);
  }
});

test('allowed samples (loopback, documentation ranges, versions, in-flight, file names) are not reported', () => {
  for (const line of samples.allowed) {
    assert.deepEqual(ruleIdsFor(NEUTRAL_PATH, line), [], `unexpected finding for: ${line}`);
  }
});

test('the brand term is allowed under provenance/ and docs/adr/ but nowhere else', () => {
  const { provenancePath, adrPath, brandLine } = samples.exceptions;
  assert.deepEqual(ruleIdsFor(provenancePath, brandLine), []);
  assert.deepEqual(ruleIdsFor(adrPath, brandLine), []);
  assert.ok(ruleIdsFor('docs/standards/x.md', brandLine).includes('term-brand-product'));
});

test('the gateway extension-point doc may name integrations but not the brand', () => {
  const { gatewayDocPath, integrationLine, brandLine } = samples.exceptions;
  assert.deepEqual(ruleIdsFor(gatewayDocPath, integrationLine), []);
  assert.ok(ruleIdsFor(gatewayDocPath, brandLine).includes('term-brand-product'));
});

test('the DGA check covers every file type, prose included (phase 2 exit), but not provenance or ADRs', () => {
  const { packageJsonDependency, devPackage, tarballPath } = samples.dga;
  assert.ok(ruleIdsFor('package.json', packageJsonDependency).includes('dga-package'));
  assert.ok(ruleIdsFor('frontend/app/main.ts', devPackage).includes('dga-package'));
  assert.ok(ruleIdsFor('frontend/styles.scss', devPackage).includes('dga-package'));
  assert.ok(ruleIdsFor('docs/notes.md', packageJsonDependency).includes('dga-package'));
  assert.ok(ruleIdsFor('CLAUDE.md', devPackage).includes('dga-package'));
  assert.deepEqual(ruleIdsFor(samples.exceptions.adrPath, devPackage), []);
  assert.deepEqual(ruleIdsFor(samples.exceptions.provenancePath, devPackage), []);
  assert.ok(ruleIdsFor(tarballPath, null).includes('dga-tarball'), 'file names are scanned even for binaries');
});

test('the owned DGA package, its tokens and the DGA site are not flagged as the external package', () => {
  for (const line of samples.dga.allowed) {
    assert.ok(!ruleIdsFor('frontend/starter-web/angular.json', line).includes('dga-package'), line);
    assert.ok(!ruleIdsFor('docs/design/tokens.md', line).includes('dga-package'), line);
  }
});

test('term rules also scan file names', () => {
  const findings = scrubFile(samples.exceptions.brandPath, '', configuration);
  assert.equal(findings[0]?.ruleId, 'term-brand-product');
  assert.equal(findings[0]?.line, 0);
});

test('national-ID findings mask the number in the excerpt', () => {
  const sample = samples.flagged.find((entry) => entry.rule === 'national-id');
  const [finding] = scrubFile(NEUTRAL_PATH, sample.line, configuration);
  const digits = sample.line.match(/\d{10}/)[0];
  assert.ok(!finding.excerpt.includes(digits));
  assert.ok(finding.excerpt.includes('*'));
});

test('CLI exits 1 with findings and 0 on a clean tree', (testContext) => {
  const dirty = makeTempDirectory(testContext);
  writeTree(dirty, { 'a/clean.md': 'nothing to see\n', 'b/dirty.cs': `${samples.flagged[0].line}\n` });
  const failing = runTool('scrub-sweep.mjs', ['--root', dirty]);
  assert.equal(failing.status, EXIT_FINDINGS, failing.stderr);
  assert.match(failing.stdout, /b\/dirty\.cs:1:\d+\s+term-brand-product/);

  const clean = makeTempDirectory(testContext);
  writeTree(clean, { 'readme.md': 'Starter docs.\n' });
  const passing = runTool('scrub-sweep.mjs', ['--root', clean]);
  assert.equal(passing.status, EXIT_CLEAN, passing.stdout + passing.stderr);
});

test('CLI exits 2 on a malformed term list', (testContext) => {
  const directory = makeTempDirectory(testContext);
  writeTree(directory, { 'terms.txt': 'not a rule line\n', 'tree/a.md': 'x\n' });
  const result = runTool('scrub-sweep.mjs', ['--root', join(directory, 'tree'), '--terms', join(directory, 'terms.txt')]);
  assert.equal(result.status, 2);
  assert.match(result.stderr, /expected "<id>\[\/<flags>\]: <regex>"/);
});

test('the sweep does not flag its own tool folder (term list, exceptions, manifest, code, tests)', () => {
  const { findings } = scrubTree(REPO_ROOT, configuration);
  assert.deepEqual(findings.filter((finding) => finding.path.startsWith(OWN_FOLDER)), []);
});
