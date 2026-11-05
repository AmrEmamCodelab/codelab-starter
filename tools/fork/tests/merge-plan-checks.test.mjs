// Unit tests of the upstream-merge plan, the fork-model checks and the CLI entry. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { main } from '../fork.mjs';
import { parseDiff } from '../lib/diff.mjs';
import { appendProbe, canProbe, checkAssets, checkNpmLock, checkNuGetOrigins, compareSets, isCodelabPackageFolder, resolveByUnion } from '../lib/fork-model-checks.mjs';
import { internals } from '../lib/fork-model.mjs';
import { ConflictKind, createContentTransform, DropReason, isModuleListFile, isRegeneratedOutput, planPatches } from '../lib/merge-plan.mjs';
import { createRemovedPathMatcher, createRemovedProjectLineMatcher } from '../lib/modules.mjs';
import { createRenamer } from '../lib/rename.mjs';

const BLOB = 'a'.repeat(40);
const ZERO = '0'.repeat(40);

function modification(path) {
  return `diff --git a/${path} b/${path}\nindex ${BLOB}..${BLOB} 100644\n--- a/${path}\n+++ b/${path}\n@@ -1 +1 @@\n-x\n+y\n`;
}

function addition(path) {
  return `diff --git a/${path} b/${path}\nnew file mode 100644\nindex ${ZERO}..${BLOB}\n--- /dev/null\n+++ b/${path}\n@@ -0,0 +1 @@\n+y\n`;
}

function deletion(path) {
  return `diff --git a/${path} b/${path}\ndeleted file mode 100644\nindex ${BLOB}..${ZERO}\n--- a/${path}\n+++ /dev/null\n@@ -1 +0,0 @@\n-x\n`;
}

describe('planPatches', () => {
  const renamer = createRenamer([{ from: 'Starter', to: 'Acme' }]);
  const forkFiles = new Set(['docs/a.md', 'backend/src/Acme.Api/Program.cs', 'docs/exists.md', 'backend/src/Modules/A/module.rules.yaml', 'CLAUDE.md']);
  const context = {
    renamer,
    forkFiles,
    isRemovedPath: createRemovedPathMatcher(['B']),
    hasFile: (file) => forkFiles.has(file),
  };
  const diff = [
    modification('docs/a.md'),
    modification('backend/src/Starter.Api/Program.cs'),
    addition('docs/new.md'),
    modification('backend/src/Modules/B/MODULE.md'),
    addition('backend/src/Modules/B/New.cs'),
    addition('packages/x/y.md'),
    modification('rules/rules.compiled.json'),
    modification('backend/src/Modules/A/CLAUDE.md'),
    modification('CLAUDE.md'),
    modification('docs/deleted-in-fork.md'),
    deletion('docs/gone-in-both.md'),
    addition('docs/exists.md'),
  ].join('');
  const plan = planPatches(parseDiff(diff), context);

  it('keeps fork files (renamed) and new files', () => {
    assert.deepEqual(plan.keep.map((entry) => entry.patch.path), ['docs/a.md', 'backend/src/Acme.Api/Program.cs', 'docs/new.md', 'CLAUDE.md']);
    assert.equal(plan.keep[1].upstreamPath, 'backend/src/Starter.Api/Program.cs');
  });

  it('drops removed areas, regenerated outputs and deletions the fork already has', () => {
    assert.deepEqual(plan.dropped.map((item) => [item.path, item.reason]), [
      ['backend/src/Modules/B/MODULE.md', DropReason.removedArea],
      ['backend/src/Modules/B/New.cs', DropReason.removedArea],
      ['packages/x/y.md', DropReason.removedArea],
      ['rules/rules.compiled.json', DropReason.regeneratedOutput],
      ['backend/src/Modules/A/CLAUDE.md', DropReason.regeneratedOutput],
      ['docs/gone-in-both.md', DropReason.alreadyDeleted],
    ]);
  });

  it('reports modify/delete and add/add as conflicts', () => {
    assert.deepEqual(plan.conflicts, [
      { path: 'docs/deleted-in-fork.md', kind: ConflictKind.deletedInFork },
      { path: 'docs/exists.md', kind: ConflictKind.existsInFork },
    ]);
  });
});

describe('content transform', () => {
  const renamer = createRenamer([{ from: 'Starter', to: 'Acme' }]);

  // In a renamed fork its own (renamed) constants name the module projects and the module-list files, so the matcher
  // works in fork naming. These tests run the starter's constants, so line filtering is tested without a rename.
  it('drops removed-module lines only in the module list files', () => {
    const transform = createContentTransform(createRenamer([]), createRemovedProjectLineMatcher(['B']));
    const slnx = '<Project Path="backend/src/Modules/A/Starter.Modules.A.csproj" />\n<Project Path="backend/src/Modules/B/Starter.Modules.B.csproj" />\n';
    assert.equal(transform('codelab-starter.slnx', slnx), '<Project Path="backend/src/Modules/A/Starter.Modules.A.csproj" />\n');
    assert.equal(transform('docs/x.md', slnx), slnx);
  });

  it('renames every file through the renameMap', () => {
    const transform = createContentTransform(renamer, null);
    assert.equal(transform('docs/x.md', 'namespace Starter.Api;'), 'namespace Acme.Api;');
    assert.equal(isModuleListFile('backend/src/Starter.Api/Starter.Api.csproj'), true);
    assert.equal(isModuleListFile('docs/x.md'), false);
  });

  it('identifies rules-engine outputs', () => {
    const hasRules = (file) => file === 'backend/src/Modules/A/module.rules.yaml';
    assert.equal(isRegeneratedOutput('rules/eslint-boundaries.compiled.json', hasRules), true);
    assert.equal(isRegeneratedOutput('backend/src/Modules/A/BannedSymbols.generated.txt', hasRules), true);
    assert.equal(isRegeneratedOutput('backend/src/Modules/A/.editorconfig', hasRules), true);
    assert.equal(isRegeneratedOutput('.editorconfig', hasRules), false);
    assert.equal(isRegeneratedOutput('backend/src/Modules/A/MODULE.md', hasRules), false);
  });
});

describe('fork-model checks', () => {
  it('appends a probe comment in the file language, keeping line endings', () => {
    assert.equal(appendProbe('# T\n', 'a.md', 'upstream'), '# T\n<!-- fork-model probe: upstream -->\n');
    assert.equal(appendProbe('x\r\ny', 'a.cs', 'fork'), 'x\r\ny\r\n// fork-model probe: fork\r\n');
    assert.equal(appendProbe('', 'a.ps1', 'fork'), '# fork-model probe: fork\n');
    assert.equal(canProbe('a.json'), false);
    assert.throws(() => appendProbe('{}', 'a.json', 'x'), /no probe comment style/);
  });

  it('resolves conflicts by union, ours first (merge and diff3 styles, CRLF)', () => {
    const text = 'a\n<<<<<<< ours\nmine\n=======\ntheirs\n>>>>>>> theirs\nz\n';
    assert.equal(resolveByUnion(text), 'a\nmine\ntheirs\nz\n');
    const diff3 = 'a\r\n<<<<<<< ours\r\nmine\r\n||||||| base\r\nold\r\n=======\r\ntheirs\r\n>>>>>>> theirs\r\n';
    assert.equal(resolveByUnion(diff3), 'a\r\nmine\r\ntheirs\r\n');
  });

  it('compares path sets', () => {
    assert.deepEqual(compareSets(['a', 'b'], ['b', 'c']), { missing: ['a'], unexpected: ['c'] });
  });

  it('accepts Codelab packages restored from the fork feed and refuses project references or other sources', () => {
    const feed = 'C:\\fork\\feed\\nuget';
    const assets = { libraries: { 'Codelab.Security/0.5.1': { type: 'package' }, 'Serilog/4.4.0': { type: 'package' } }, project: { restore: { sources: { 'C:\\fork\\feed\\nuget\\': {}, 'https://api.nuget.org/v3/index.json': {} } } } };
    assert.deepEqual(checkAssets(assets, feed, 'x'), []);
    const asProject = { libraries: { 'Codelab.Security/1.0.0': { type: 'project' } }, project: { restore: { sources: {} } } };
    assert.equal(checkAssets(asProject, feed, 'x').length, 2);
    assert.deepEqual(checkNuGetOrigins([{ id: 'codelab.security', version: '0.5.1', metadata: { source: 'C:\\fork\\feed\\nuget' } }], feed), []);
    assert.equal(checkNuGetOrigins([{ id: 'codelab.security', version: '0.5.1', metadata: { source: 'https://api.nuget.org/v3/index.json' } }], feed).length, 1);
    assert.equal(isCodelabPackageFolder('codelab.errors'), true);
    assert.equal(isCodelabPackageFolder('serilog'), false);
  });

  it('accepts @codelab npm packages only from feed tarballs', () => {
    const good = { packages: { '': {}, 'node_modules/@codelab/ng-security': { resolved: 'file:feed/npm/codelab-ng-security-0.5.1.tgz' }, 'node_modules/rxjs': { resolved: 'https://registry.npmjs.org/rxjs.tgz' } } };
    assert.deepEqual(checkNpmLock(good, 'feed/npm'), []);
    const bad = { packages: { 'node_modules/@codelab/ng-security': { resolved: 'packages/npm/ng-security', link: true }, 'frontend/x/node_modules/@codelab/ng-dga': { resolved: 'https://registry.npmjs.org/x.tgz' } } };
    assert.equal(checkNpmLock(bad, 'feed/npm').length, 2);
  });

  it('defaults the fork-model options and validates them', () => {
    const options = internals.withDefaults({ starterRoot: '.', editFile: [], removeModule: 'Demo' });
    assert.equal(options.editFiles.length, 5);
    assert.equal(options.conflictFile, options.editFiles[0]);
    assert.equal(options.productPrefix, 'FORKTEST');
    const bad = internals.withDefaults({ starterRoot: '.', editFile: ['a.md'], removeModule: 'Demo' });
    assert.throws(() => internals.validateOptions({ ...bad, starterRoot: process.cwd() }), /scaffold|exactly 5/);
  });
});

describe('fork.mjs main', () => {
  const quiet = () => {
    const lines = [];
    return { lines, io: { log: (line) => lines.push(line), error: (line) => lines.push(line) } };
  };

  it('returns 2 for an unknown command or a usage error', () => {
    const { lines, io } = quiet();
    assert.equal(main(['nope'], io), 2);
    assert.equal(main(['remove-module', '--root', '.'], io), 2);
    assert.equal(main(['upstream-merge', '--root', '.'], io), 2);
    assert.equal(main(['upstream-merge', '--root', '.', '--continue', '--upstream', 'x'], io), 2);
    assert.ok(lines.some((line) => /--name is required/.test(line)));
  });
});
