// tools/fork/Test-ForkModel.ps1 against a synthetic starter (-StarterRoot): wave 1 has no backend or frontend, so
// the feed-mode build is skipped (-SkipBuild); its checks are unit-tested in fork-model-checks.test.mjs.
// Run: node --test "tools/fork/tests/**/*.test.mjs" (spawns Windows PowerShell 5.1).
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';
import { makeTempDir, runScript } from './support/powershell.mjs';
import { createFeed, createSyntheticStarter, EDIT_FILES, REPO_ROOT } from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0, failed: 1, usage: 2 });
const SCRIPT = join(REPO_ROOT, 'tools/fork/Test-ForkModel.ps1');

function forkModelArgs(starter, feed, extra = []) {
  return ['-StarterRoot', starter, '-RemoveModule', 'Demo', '-FeedDir', feed, '-EditFile', EDIT_FILES.join(','),
    '-AddFile', 'docs/six.md', '-SkipBuild', '-SkipSecrets', '-SkipNpmLock', ...extra];
}

describe('Test-ForkModel.ps1 on a synthetic starter', () => {
  const temp = makeTempDir();
  const feed = createFeed(join(temp.path, 'feed-source'));
  after(() => temp.cleanup());

  it('passes: scaffold, init, upstream change, one expected conflict, resolve, continue', () => {
    const starter = join(temp.path, 'starter');
    createSyntheticStarter(starter);
    const result = runScript(SCRIPT, forkModelArgs(starter, feed));
    assert.equal(result.code, EXIT.ok, result.output);
    assert.match(result.output, /Fork-model test PASSED/);
  });

  it('honours -ConflictFile (another of the five edits is the expected conflict)', () => {
    const starter = join(temp.path, 'starter-mismatch');
    createSyntheticStarter(starter);
    const result = runScript(SCRIPT, forkModelArgs(starter, feed, ['-ConflictFile', EDIT_FILES[2]]));
    assert.equal(result.code, EXIT.ok, result.output);
  });

  it('exits 2 with a clear message when the scaffolder does not exist yet', () => {
    const starter = join(temp.path, 'starter-no-scaffold');
    createSyntheticStarter(starter, { withoutScaffolder: true });
    const result = runScript(SCRIPT, forkModelArgs(starter, feed));
    assert.equal(result.code, EXIT.usage, result.output);
    assert.match(result.output, /tools\/scaffold\/new-module\.mjs not found/);
  });

  it('exits 2 when the edit list is not exactly 5 files', () => {
    const starter = join(temp.path, 'starter-short');
    createSyntheticStarter(starter);
    const args = forkModelArgs(starter, feed).map((value) => (value === EDIT_FILES.join(',') ? EDIT_FILES.slice(1).join(',') : value));
    const result = runScript(SCRIPT, args);
    assert.equal(result.code, EXIT.usage, result.output);
    assert.match(result.output, /exactly 5 --edit-file/);
  });
});
