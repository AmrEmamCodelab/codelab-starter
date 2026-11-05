// Refusals of init.ps1, tools/remove-module.ps1 and tools/upstream-merge.ps1: each exits 1 (or 2 for usage) and
// leaves the repo exactly as it was. Run: node --test "tools/fork/tests/**/*.test.mjs" (Windows PowerShell 5.1).
import assert from 'node:assert/strict';
import { existsSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { makeTempDir, runScript } from './support/powershell.mjs';
import { cloneFork, commitAll, createFeed, createSyntheticStarter, git, read, write } from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0, failed: 1, usage: 2 });
const INIT_SWITCHES = ['-SkipNpmLock', '-SkipSecrets'];

describe('fork tool refusals', () => {
  const temp = makeTempDir();
  const starter = join(temp.path, 'starter');
  const feed = createFeed(join(temp.path, 'feed-source'));
  let forkCount = 0;

  before(() => createSyntheticStarter(starter));
  after(() => temp.cleanup());

  function newFork() {
    forkCount += 1;
    return cloneFork(starter, join(temp.path, `fork-${forkCount}`));
  }

  function init(fork, args) {
    return runScript(join(fork, 'init.ps1'), ['-Minimal', '-FeedDir', feed, ...INIT_SWITCHES, ...args], { cwd: fork });
  }

  function assertUnchanged(fork, head) {
    assert.equal(git(fork, ['rev-parse', 'HEAD']), head);
    assert.equal(git(fork, ['status', '--porcelain']), '');
  }

  it('init refuses a required module and changes nothing', () => {
    const fork = newFork();
    const head = git(fork, ['rev-parse', 'HEAD']);
    const result = init(fork, ['-ProductPrefix', 'ACME', '-RemoveModule', 'B,Identity']);
    assert.equal(result.code, EXIT.failed, result.output);
    assert.match(result.output, /'Identity' is a required module/);
    assertUnchanged(fork, head);
  });

  it('init refuses an unknown module before removing any other', () => {
    const fork = newFork();
    const head = git(fork, ['rev-parse', 'HEAD']);
    const result = init(fork, ['-ProductPrefix', 'ACME', '-RemoveModule', 'B,Nowhere']);
    assert.equal(result.code, EXIT.failed, result.output);
    assert.match(result.output, /Module Nowhere not found/);
    assertUnchanged(fork, head);
    assert.ok(existsSync(join(fork, 'backend/src/Modules/B')));
  });

  for (const [prefix, reason] of [['acme', /is invalid/], ['A', /is invalid/], ['ACME-1', /is invalid/], ['STARTER', /is reserved/], ['CODELABTEST', /is reserved/]]) {
    it(`init refuses the bad prefix '${prefix}'`, () => {
      const fork = newFork();
      const head = git(fork, ['rev-parse', 'HEAD']);
      const result = init(fork, ['-ProductPrefix', prefix]);
      assert.equal(result.code, EXIT.failed, result.output);
      assert.match(result.output, reason);
      assertUnchanged(fork, head);
    });
  }

  it('init refuses an already-initialized fork', () => {
    const fork = newFork();
    assert.equal(init(fork, ['-ProductPrefix', 'ACME']).code, EXIT.ok);
    const head = git(fork, ['rev-parse', 'HEAD']);
    const again = init(fork, ['-ProductPrefix', 'OTHER']);
    assert.equal(again.code, EXIT.failed, again.output);
    assert.match(again.output, /already an initialized fork/);
    assertUnchanged(fork, head);
  });

  it('init without -Minimal is a usage error', () => {
    const fork = newFork();
    const result = runScript(join(fork, 'init.ps1'), ['-ProductPrefix', 'ACME'], { cwd: fork });
    assert.equal(result.code, EXIT.usage, result.output);
  });

  it('init refuses a dirty work tree', () => {
    const fork = newFork();
    write(fork, 'docs/one.md', 'changed\n');
    const result = init(fork, ['-ProductPrefix', 'ACME']);
    assert.equal(result.code, EXIT.failed, result.output);
    assert.match(result.output, /uncommitted changes/);
  });

  it('init refuses a feed that lacks a version', () => {
    const fork = newFork();
    const emptyFeed = join(temp.path, 'empty-feed');
    const result = runScript(join(fork, 'init.ps1'), ['-Minimal', '-ProductPrefix', 'ACME', '-FeedDir', emptyFeed, ...INIT_SWITCHES], { cwd: fork });
    assert.equal(result.code, EXIT.failed, result.output);
    assert.match(result.output, /lacks: Codelab\.Build\.Sdk 0\.2\.1/);
  });

  it('init refuses a missing target unless -AllowMissingTargets names the skip', () => {
    const fork = newFork();
    rmSync(join(fork, 'frontend'), { recursive: true });
    const head = commitAll(fork, 'no frontend');
    const refused = init(fork, ['-ProductPrefix', 'ACME']);
    assert.equal(refused.code, EXIT.failed, refused.output);
    assert.match(refused.output, /Missing target file\(s\): frontend\/starter-web\/public\/config\.json, frontend\/starter-web\/package\.json/);
    assertUnchanged(fork, head);
    const allowed = init(fork, ['-ProductPrefix', 'ACME', '-AllowMissingTargets']);
    assert.equal(allowed.code, EXIT.ok, allowed.output);
    assert.match(allowed.output, /skipped prefix target frontend\/starter-web\/public\/config\.json/);
  });

  it('remove-module refuses required modules, kernel projects and unknown modules', () => {
    const fork = newFork();
    const script = join(fork, 'tools/remove-module.ps1');
    for (const [name, reason] of [['Access', /required module/], ['Api', /kernel project/], ['Infrastructure', /kernel project/], ['bad-name', /is not a module name/], ['Missing', /not found/]]) {
      const result = runScript(script, ['-Name', name], { cwd: fork });
      assert.equal(result.code, EXIT.failed, `${name}: ${result.output}`);
      assert.match(result.output, reason);
    }
    assert.equal(git(fork, ['status', '--porcelain']), '');
  });

  it('remove-module removes a module, records it, and is a no-op the second time', () => {
    const fork = newFork();
    const script = join(fork, 'tools/remove-module.ps1');
    const first = runScript(script, ['-Name', 'B'], { cwd: fork });
    assert.equal(first.code, EXIT.ok, first.output);
    assert.deepEqual(JSON.parse(read(fork, 'codelab.starter.json')).removedModules, ['B']);
    assert.equal(existsSync(join(fork, 'backend/tests/Modules/Starter.Modules.B.Tests')), false);
    assert.deepEqual(JSON.parse(read(fork, 'rules/rules.compiled.json')).modules, ['A']);
    const second = runScript(script, ['-Name', 'B'], { cwd: fork });
    assert.equal(second.code, EXIT.ok, second.output);
    assert.match(second.output, /already removed/);
  });

  it('upstream-merge refuses a fork that was never initialized, and a missing -Upstream is a usage error', () => {
    const fork = newFork();
    const script = join(fork, 'tools/upstream-merge.ps1');
    const notInitialized = runScript(script, ['-Upstream', starter, '-SkipBuild'], { cwd: fork });
    assert.equal(notInitialized.code, EXIT.failed, notInitialized.output);
    assert.match(notInitialized.output, /run init\.ps1/);
    assert.equal(runScript(script, ['-SkipBuild'], { cwd: fork }).code, EXIT.usage);
  });

  it('upstream-merge reports a failed build with exit 1', () => {
    const fork = newFork();
    assert.equal(init(fork, ['-ProductPrefix', 'ACME']).code, EXIT.ok);
    write(starter, 'docs/two.md', `${read(starter, 'docs/two.md')}More.\n`);
    commitAll(starter, 'upstream doc');
    const result = runScript(join(fork, 'tools/upstream-merge.ps1'), ['-Upstream', starter, '-BuildCommand', 'exit 3'], { cwd: fork });
    assert.equal(result.code, EXIT.failed, result.output);
    assert.match(result.output, /build failed/);
  });
});
