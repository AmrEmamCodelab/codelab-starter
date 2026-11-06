// Tests of tools/ci/module-deletability.ps1: argument handling, the temp-copy guard (it never removes inside the
// source repo, around it, or in a folder it did not create) and the report of a failed check, on synthetic repos in
// %TEMP%. Run: node --test "tools/ci/tests/**/*.test.mjs" (spawns Windows PowerShell 5.1).
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { makeTempDir, runScript } from '../../fork/tests/support/powershell.mjs';
import { createSyntheticStarter, git } from '../../fork/tests/support/synthetic-starter.mjs';

const SCRIPT = fileURLToPath(new URL('../module-deletability.ps1', import.meta.url));
const REPO_ROOT = fileURLToPath(new URL('../../../', import.meta.url)).replace(/[\\/]$/, '');
const EXIT = Object.freeze({ passed: 0, checkFailed: 1, usage: 2 });
const MARKER = '.codelab-module-deletability';

const run = (args) => runScript(SCRIPT, args, { cwd: REPO_ROOT });

describe('module-deletability.ps1 arguments', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());
  const workDir = (name) => join(temp.path, name);

  for (const [label, args, message] of [
    ['a missing -Name', [], /-Name is required/],
    ['a name that is not PascalCase', ['-Name', 'service-requests'], /is not a module name/],
    ['an unknown argument', ['-Name', 'Demo', '-Bogus', '1'], /Unknown argument\(s\): -Bogus 1/],
    ['a positional argument', ['Demo'], /Unknown argument\(s\): Demo/],
    ['-Overlay without -Revision', ['-Name', 'Demo', '-Overlay', 'tools\\fork'], /-Overlay needs -Revision/],
    ['an -Overlay outside the repo', ['-Name', 'Demo', '-Revision', 'HEAD', '-Overlay', '..\\elsewhere'], /must be a repo-relative path inside the repo/],
    ['a source that is not a starter repo', ['-Name', 'Demo', '-SourceRoot', tmpdir()], /is not a starter repo: codelab-starter\.slnx not found/],
  ]) {
    it(`refuses ${label} with exit 2 and the usage line`, () => {
      const result = run([...args, '-WorkDir', workDir('unused'), '-PlanOnly']);
      assert.equal(result.code, EXIT.usage, result.output);
      assert.match(result.output, message);
      assert.match(result.output, /Usage: tools\\ci\\module-deletability\.ps1 -Name <PascalName>/);
      assert.equal(existsSync(workDir('unused')), false);
    });
  }

  it('prints the plan for valid arguments and creates nothing (-PlanOnly)', () => {
    const work = workDir('plan');
    const result = run(['-Name', 'ServiceRequests', '-WorkDir', work, '-PlanOnly']);
    assert.equal(result.code, EXIT.passed, result.output);
    assert.match(result.output, /Module deletability: ServiceRequests/);
    assert.ok(result.output.includes(`copy:       ${join(work, 'repo')}`), result.output);
    assert.ok(result.output.includes(`artifacts:  ${join(work, 'repo', 'artifacts')}`), result.output);
    for (const step of ['copy', 'node-modules', 'remove', 'verify-removed', 'dotnet-build', 'backend-tests', 'frontend-tests', 'frontend-build']) {
      assert.match(result.output, new RegExp(`step ${step}\\s+(setup|check)`));
    }
    assert.match(result.output, /step probe\s+SKIPPED/);
    assert.equal(existsSync(work), false);
  });

  it('plans the probe with -DummyModule and skips what -SkipBackendTests and -SkipFrontend name', () => {
    const result = run(['-Name', 'DeletionProbe', '-WorkDir', workDir('plan2'), '-DummyModule', '-SkipBackendTests', '-SkipFrontend', '-PlanOnly']);
    assert.equal(result.code, EXIT.passed, result.output);
    assert.match(result.output, /step probe\s+setup/);
    for (const step of ['backend-tests', 'frontend-tests', 'frontend-build']) assert.match(result.output, new RegExp(`step ${step}\\s+SKIPPED`));
  });

  it('defaults the work folder to %TEMP%\\codelab-module-deletability-<Name>', () => {
    const result = run(['-Name', 'Demo', '-PlanOnly']);
    assert.equal(result.code, EXIT.passed, result.output);
    assert.ok(result.output.includes(`work:       ${join(tmpdir(), 'codelab-module-deletability-Demo')}`), result.output);
  });
});

describe('module-deletability.ps1 temp-copy guard', () => {
  const temp = makeTempDir();
  const source = join(temp.path, 'source');
  before(() => createSyntheticStarter(source));
  after(() => temp.cleanup());

  const runOn = (work, extra = []) => runScript(SCRIPT, ['-Name', 'B', '-SourceRoot', source, '-WorkDir', work, ...extra], { cwd: temp.path });
  const sourceStatus = () => git(source, ['status', '--porcelain']);

  for (const [label, work, message] of [
    ['the source repo itself', () => source, /the work folder .* is the source repo or inside it/],
    ['a folder inside the source repo', () => join(source, 'scratch'), /the work folder .* is the source repo or inside it/],
    ['a folder that contains the source repo', () => temp.path, /the work folder .* contains the source repo/],
  ]) {
    it(`refuses ${label} before writing anything (no -PlanOnly)`, () => {
      const result = runOn(work());
      assert.equal(result.code, EXIT.usage, result.output);
      assert.match(result.output, message);
      assert.equal(sourceStatus(), '');
      assert.equal(existsSync(join(source, 'scratch')), false);
      assert.ok(existsSync(join(source, 'backend/src/Modules/B')));
    });
  }

  it('refuses the real repo as the work folder', () => {
    const result = run(['-Name', 'Demo', '-WorkDir', REPO_ROOT, '-PlanOnly']);
    assert.equal(result.code, EXIT.usage, result.output);
    assert.match(result.output, /REFUSED: the work folder .* is the source repo or inside it/);
  });

  it('refuses an existing, non-empty folder it did not create, and leaves its files alone', () => {
    const work = join(temp.path, 'someone-elses');
    mkdirSync(work);
    writeFileSync(join(work, 'keep.txt'), 'mine');
    const result = runOn(work);
    assert.equal(result.code, EXIT.usage, result.output);
    assert.match(result.output, /exists, is not empty and was not created by this script/);
    assert.equal(readFileSync(join(work, 'keep.txt'), 'utf8'), 'mine');
  });

  it('reuses a folder that carries its marker, and an empty folder', () => {
    const marked = join(temp.path, 'marked');
    mkdirSync(marked);
    writeFileSync(join(marked, MARKER), 'x');
    writeFileSync(join(marked, 'old.txt'), 'old');
    assert.equal(runOn(marked, ['-PlanOnly']).code, EXIT.passed);
    const empty = join(temp.path, 'empty');
    mkdirSync(empty);
    assert.equal(runOn(empty, ['-PlanOnly']).code, EXIT.passed);
  });

  it('removes the module in the copy only, and names the failing check with exit 1', () => {
    mkdirSync(join(source, 'node_modules'), { recursive: true });
    const work = join(temp.path, 'work');
    const result = runOn(work, ['-SkipBackendTests', '-SkipFrontend']);
    assert.equal(result.code, EXIT.checkFailed, result.output);
    assert.match(result.output, /remove\s+PASSED/);
    assert.match(result.output, /verify-removed\s+PASSED/);
    assert.match(result.output, /RESULT: FAILED at step 'dotnet-build' \(check\)/);
    const copy = join(work, 'repo');
    assert.equal(existsSync(join(copy, 'backend/src/Modules/B')), false);
    assert.equal(existsSync(join(copy, '.git')), false);
    assert.deepEqual(JSON.parse(readFileSync(join(copy, 'codelab.starter.json'), 'utf8')).removedModules, ['B']);
    assert.ok(existsSync(join(work, MARKER)));
    assert.ok(existsSync(join(source, 'backend/src/Modules/B')));
    assert.equal(sourceStatus(), '');
    assert.deepEqual(readdirSync(join(source, 'node_modules')), []);
  });
});
