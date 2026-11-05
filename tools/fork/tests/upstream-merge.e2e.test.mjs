// End to end on a synthetic starter: init -Minimal removes B and packages/, upstream edits 5 files, adds 1, touches
// B and packages/ (and adds a module C next to B's lines), the fork edited one file on the same lines, then
// upstream-merge runs. Exactly that file conflicts; everything else applies or is dropped as the fork model says.
// Run: node --test "tools/fork/tests/**/*.test.mjs" (spawns Windows PowerShell 5.1).
import assert from 'node:assert/strict';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { hashContent } from '../lib/hashing.mjs';
import { makeTempDir, runScript } from './support/powershell.mjs';
import {
  cloneFork,
  commitAll,
  createFeed,
  createSyntheticStarter,
  EDIT_FILES,
  git,
  read,
  write,
} from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0, failed: 1, usage: 2 });
const CONFLICT_FILE = EDIT_FILES[0];
const ADDED_FILE = 'docs/six.md';
const BUILD_MARKER = 'build-ran.txt';

function appendLine(root, file, line) {
  write(root, file, `${read(root, file)}${line}\n`);
}

function addModuleC(starter) {
  write(starter, 'backend/src/Modules/C/Starter.Modules.C.csproj', '<Project Sdk="Microsoft.NET.Sdk">\r\n</Project>\r\n');
  write(starter, 'backend/src/Modules/C/CModule.cs', 'namespace Starter.Modules.C;\n');
  const slnxLine = '    <Project Path="backend/src/Modules/B/Starter.Modules.B.csproj" />';
  write(starter, 'codelab-starter.slnx', read(starter, 'codelab-starter.slnx').replace(slnxLine, `${slnxLine}\r\n    <Project Path="backend/src/Modules/C/Starter.Modules.C.csproj" />`));
  const hostLine = '    <ProjectReference Include="..\\Modules\\B\\Starter.Modules.B.csproj" />';
  write(starter, 'backend/src/Starter.Api/Starter.Api.csproj', read(starter, 'backend/src/Starter.Api/Starter.Api.csproj').replace(hostLine, `${hostLine}\r\n    <ProjectReference Include="..\\Modules\\C\\Starter.Modules.C.csproj" />`));
}

/** The upstream commit: 5 edits, 1 add, B touched (edit + add), packages/ touched (edit + add), module C added. */
function changeUpstream(starter) {
  for (const file of EDIT_FILES) appendLine(starter, file, 'Upstream line.');
  write(starter, ADDED_FILE, '# Doc 6\n\nAdded upstream.\n');
  appendLine(starter, 'backend/src/Modules/B/MODULE.md', 'Upstream note for B.');
  write(starter, 'backend/src/Modules/B/BAdded.cs', 'namespace Starter.Modules.B;\n');
  appendLine(starter, 'packages/README.md', 'Upstream package note.');
  write(starter, 'packages/dotnet/Codelab.New/README.md', 'New package.\n');
  addModuleC(starter);
  git(starter, ['add', '-A']);
  // The starter regenerates its rules outputs (the stub lists the module folders: A, B, C).
  const compile = join(starter, 'tools/rules-engine/compile.mjs');
  writeFileSync(join(starter, 'rules/rules.compiled.json'), `${JSON.stringify({ modules: ['A', 'B', 'C'] }, null, 2)}\n`);
  assert.ok(existsSync(compile));
  return commitAll(starter, 'upstream change');
}

describe('init -Minimal then upstream-merge on a synthetic starter', () => {
  const temp = makeTempDir();
  const starter = join(temp.path, 'starter');
  const fork = join(temp.path, 'fork');
  const feed = createFeed(join(temp.path, 'feed-source'));
  const reportPath = join(temp.path, 'report.json');
  let initResult;
  let starterBase;

  before(() => {
    starterBase = createSyntheticStarter(starter);
    cloneFork(starter, fork);
    initResult = runScript(join(fork, 'init.ps1'), ['-Minimal', '-ProductPrefix', 'ACME', '-RemoveModule', 'B', '-FeedDir', feed, '-SkipNpmLock', '-SkipSecrets'], { cwd: fork });
  });
  after(() => temp.cleanup());

  it('init exits 0 and makes exactly one commit with the recorded message', () => {
    assert.equal(initResult.code, EXIT.ok, initResult.output);
    assert.equal(git(fork, ['log', '-1', '--format=%s']), 'init: minimal fork (prefix ACME, removed B)');
    assert.equal(git(fork, ['rev-list', '--count', 'HEAD']), '2');
    assert.equal(git(fork, ['status', '--porcelain']), '');
  });

  it('init removed module B (folders, solution and host lines) and packages/', () => {
    for (const gone of ['packages', 'backend/src/Modules/B', 'backend/tests/Modules/Starter.Modules.B.Tests', 'frontend/starter-web/src/app/features/b']) {
      assert.equal(existsSync(join(fork, gone)), false, gone);
    }
    const slnx = read(fork, 'codelab-starter.slnx');
    assert.doesNotMatch(slnx, /Modules\.B|packages\//);
    assert.match(slnx, /Starter\.Modules\.A\.csproj/);
    assert.doesNotMatch(read(fork, 'backend/src/Starter.Api/Starter.Api.csproj'), /Modules\\B/);
    assert.ok(existsSync(join(fork, 'frontend/starter-web/src/app/features/a/a.page.ts')));
  });

  it('init switched to feed mode and copied the feed packages (gitignored)', () => {
    const buildProps = read(fork, 'Directory.Build.props');
    assert.match(buildProps, />false<\/UseCodelabPackageSource>/);
    assert.match(buildProps, />0\.2\.1-ci\.3<\/CodelabBuildSdkVersion>/);
    assert.match(read(fork, 'Directory.Packages.props'), /Include="Codelab\.Security" Version="0\.5\.1"/);
    const frontend = JSON.parse(read(fork, 'frontend/starter-web/package.json'));
    assert.equal(frontend.dependencies['@codelab/ng-security'], 'file:../../feed/npm/codelab-ng-security-0.5.1.tgz');
    assert.equal(frontend.dependencies.tslib, '2.9.1');
    assert.deepEqual(JSON.parse(read(fork, 'package.json')).workspaces, ['frontend/starter-web']);
    for (const file of ['feed/nuget/Codelab.Build.Sdk.0.2.1-ci.3.nupkg', 'feed/nuget/Codelab.Security.0.5.1.nupkg', 'feed/npm/codelab-ng-security-0.5.1.tgz']) {
      assert.ok(existsSync(join(fork, file)), file);
    }
    assert.equal(git(fork, ['ls-files', 'feed']).split('\n').filter((line) => /\.(nupkg|tgz)$/.test(line)).length, 0);
  });

  it('init set the prefix, regenerated the rules outputs and wrote the manifest', () => {
    assert.equal(JSON.parse(read(fork, 'backend/src/Starter.Api/appsettings.json')).Codelab.Security.Protocol.Prefix, 'ACME');
    assert.equal(JSON.parse(read(fork, 'frontend/starter-web/public/config.json')).security.protocolPrefix, 'ACME');
    assert.deepEqual(JSON.parse(read(fork, 'rules/rules.compiled.json')).modules, ['A']);
    const manifest = JSON.parse(read(fork, 'codelab.starter.json'));
    assert.equal(manifest.starterBase, starterBase);
    assert.equal(manifest.productPrefix, 'ACME');
    assert.deepEqual(manifest.removedModules, ['B']);
    assert.deepEqual(manifest.renameMap, []);
    assert.equal(manifest.hashes['docs/one.md'], hashContent(readFileSync(join(fork, 'docs/one.md'))));
    assert.equal(manifest.hashes['codelab.starter.json'], undefined);
    assert.ok(!Object.keys(manifest.hashes).some((file) => file.startsWith('packages/')));
  });

  describe('after an upstream change and a fork edit on the same lines', () => {
    let merge;
    let upstreamHead;

    before(() => {
      upstreamHead = changeUpstream(starter);
      appendLine(fork, CONFLICT_FILE, 'Fork line.');
      commitAll(fork, 'fork edit');
      merge = runScript(join(fork, 'tools/upstream-merge.ps1'), ['-Upstream', starter, '-SkipBuild', '-ReportPath', reportPath], { cwd: fork });
    });

    it('exits 1 with exactly the pre-edited file in conflict', () => {
      assert.equal(merge.code, EXIT.failed, merge.output);
      const report = JSON.parse(readFileSync(reportPath, 'utf8'));
      assert.equal(report.status, 'conflicts');
      assert.deepEqual(report.conflicts, [{ path: CONFLICT_FILE, kind: 'content' }]);
      assert.match(read(fork, CONFLICT_FILE), /<<<<<<<[\s\S]*Fork line\.[\s\S]*=======[\s\S]*Upstream line\.[\s\S]*>>>>>>>/);
    });

    it('applies the other 4 edits, the added file and module C', () => {
      const report = JSON.parse(readFileSync(reportPath, 'utf8'));
      for (const file of [...EDIT_FILES.slice(1), ADDED_FILE, 'backend/src/Modules/C/CModule.cs', 'codelab-starter.slnx', 'backend/src/Starter.Api/Starter.Api.csproj']) {
        assert.ok(report.applied.includes(file), file);
      }
      for (const file of EDIT_FILES.slice(1)) assert.match(read(fork, file), /Upstream line\.\n$/);
      assert.ok(existsSync(join(fork, ADDED_FILE)));
      const slnx = read(fork, 'codelab-starter.slnx');
      assert.match(slnx, /Starter\.Modules\.C\.csproj/);
      assert.doesNotMatch(slnx, /Starter\.Modules\.B/);
      assert.match(read(fork, 'backend/src/Starter.Api/Starter.Api.csproj'), /Modules\\C\\Starter\.Modules\.C\.csproj/);
    });

    it("drops B's changes, B's added file, packages/** and the regenerated rules outputs", () => {
      const report = JSON.parse(readFileSync(reportPath, 'utf8'));
      const dropped = Object.fromEntries(report.dropped.map((item) => [item.path, item.reason]));
      assert.equal(dropped['backend/src/Modules/B/MODULE.md'], 'removed-area');
      assert.equal(dropped['backend/src/Modules/B/BAdded.cs'], 'removed-area');
      assert.equal(dropped['packages/README.md'], 'removed-area');
      assert.equal(dropped['packages/dotnet/Codelab.New/README.md'], 'removed-area');
      assert.equal(dropped['rules/rules.compiled.json'], 'regenerated-output');
      for (const gone of ['backend/src/Modules/B', 'packages']) assert.equal(existsSync(join(fork, gone)), false, gone);
    });

    it('moves starterBase to the upstream head', () => {
      assert.equal(JSON.parse(read(fork, 'codelab.starter.json')).starterBase, upstreamHead);
    });

    it('-Continue after the human resolution recomputes hashes, regenerates and builds', () => {
      write(fork, CONFLICT_FILE, read(fork, CONFLICT_FILE).replace(/<<<<<<<[^\n]*\n|=======\n|>>>>>>>[^\n]*\n/g, ''));
      git(fork, ['add', '--', CONFLICT_FILE]);
      const command = `Set-Content -LiteralPath '${BUILD_MARKER}' -Value built`;
      const result = runScript(join(fork, 'tools/upstream-merge.ps1'), ['-Continue', '-BuildCommand', command, '-ReportPath', reportPath], { cwd: fork });
      assert.equal(result.code, EXIT.ok, result.output);
      assert.ok(existsSync(join(fork, BUILD_MARKER)), 'the -BuildCommand ran in the fork root');
      const manifest = JSON.parse(read(fork, 'codelab.starter.json'));
      assert.equal(manifest.hashes[CONFLICT_FILE], hashContent(readFileSync(join(fork, CONFLICT_FILE))));
      assert.equal(manifest.hashes[ADDED_FILE], hashContent(readFileSync(join(fork, ADDED_FILE))));
      assert.deepEqual(JSON.parse(read(fork, 'rules/rules.compiled.json')).modules, ['A', 'C']);
      assert.equal(git(fork, ['diff', '--name-only', '--diff-filter=U']), '');
    });

    it('a second run after committing is up to date', () => {
      git(fork, ['add', '-A']);
      git(fork, ['commit', '--quiet', '-m', 'merge upstream']);
      const result = runScript(join(fork, 'tools/upstream-merge.ps1'), ['-Upstream', starter, '-SkipBuild'], { cwd: fork });
      assert.equal(result.code, EXIT.ok, result.output);
      assert.match(result.output, /Already up to date/);
    });
  });
});
