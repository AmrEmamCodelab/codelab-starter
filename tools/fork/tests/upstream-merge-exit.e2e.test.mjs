// Phase 10 exit test "Upstream-merge test": a "core only" fork (the four optional modules removed by init -Minimal)
// merges a starter commit that edits 5 files, adds 1 file, touches the removed modules (their folders, their marked
// lines in shared files, their translation keys) and bumps the starter version. Expected: a clean merge (no fork
// edits, so no conflicts), the removed-module changes dropped, starterBase moved, the hashes recomputed and the
// -BuildCommand run; nothing committed.
// Run: node --test tools/fork/tests/upstream-merge-exit.e2e.test.mjs (spawns Windows PowerShell 5.1).
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { hashContent } from '../lib/hashing.mjs';
import { makeTempDir, runScript } from './support/powershell.mjs';
import { cloneFork, commitAll, createFeed, createSyntheticStarter, EDIT_FILES, git, read, write } from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0 });
/** Stand-ins for the starter's optional modules (attachments, portal, sms, ops), as PascalCase module folders. */
const OPTIONAL_MODULES = Object.freeze(['Attachments', 'Portal', 'Sms', 'Ops']);
const ADDED_FILE = 'docs/six.md';
const BUILD_MARKER = 'build-ran.txt';
const ROUTES_FILE = 'frontend/starter-web/src/app/app.routes.ts';
const EN_FILE = 'frontend/starter-web/src/assets/i18n/en.json';
const SOLUTION = 'codelab-starter.slnx';
const HOST = 'backend/src/Starter.Api/Starter.Api.csproj';
const MANIFEST = 'codelab.starter.json';
const UPSTREAM_STARTER_VERSION = '9.1.0';
const ROUTES_CLOSE = '];\n';

function appendLine(root, file, line) {
  write(root, file, `${read(root, file)}${line}\n`);
}

function readJson(root, file) {
  return JSON.parse(read(root, file));
}

/** Adds the four optional modules to the synthetic starter: folders, solution + host lines, an Ops route and key. */
function addOptionalModules(starter) {
  let slnx = read(starter, SOLUTION);
  let host = read(starter, HOST);
  for (const name of OPTIONAL_MODULES) {
    const folder = `backend/src/Modules/${name}`;
    write(starter, `${folder}/Starter.Modules.${name}.csproj`, '<Project Sdk="Microsoft.NET.Sdk">\r\n</Project>\r\n');
    write(starter, `${folder}/${name}Module.cs`, `namespace Starter.Modules.${name};\n`);
    write(starter, `${folder}/MODULE.md`, `# Module ${name}\n\n## Purpose\n\nOptional module ${name}.\n`);
    slnx = slnx.replace('  </Folder>', `    <Project Path="${folder}/Starter.Modules.${name}.csproj" />\r\n  </Folder>`);
    host = host.replace('  </ItemGroup>', `    <ProjectReference Include="..\\Modules\\${name}\\Starter.Modules.${name}.csproj" />\r\n  </ItemGroup>`);
  }
  write(starter, SOLUTION, slnx);
  write(starter, HOST, host);
  write(starter, 'frontend/starter-web/src/app/features/ops/ops.routes.ts', 'export const OPS_ROUTES = [];\n');
  write(starter, ROUTES_FILE, read(starter, ROUTES_FILE)
    .replace("import { B_ROUTES }", "import { OPS_ROUTES } from './features/ops/ops.routes'; // codelab:module Ops\nimport { B_ROUTES }")
    .replace(ROUTES_CLOSE, `  ...OPS_ROUTES, // codelab:module Ops\n${ROUTES_CLOSE}`));
  for (const language of ['en', 'ar']) {
    const file = `frontend/starter-web/src/assets/i18n/${language}.json`;
    write(starter, file, `${JSON.stringify({ ...readJson(starter, file), ops: { title: 'Ops' } }, null, 2)}\n`);
  }
  return commitAll(starter, 'optional modules');
}

/**
 * The upstream commit: 5 edits, 1 added file, and changes inside the removed modules: an edit, an added file, a new
 * marked route block for Ops next to an unmarked change in the same file, a new Ops translation key next to a kept
 * one, a new Sms project line, and a starter version bump in the manifest.
 */
function changeUpstream(starter) {
  for (const file of EDIT_FILES) appendLine(starter, file, 'Upstream line.');
  write(starter, ADDED_FILE, '# Doc 6\n\nAdded upstream.\n');
  appendLine(starter, 'backend/src/Modules/Ops/MODULE.md', 'Upstream note for Ops.');
  write(starter, 'backend/src/Modules/Sms/SmsSender.cs', 'namespace Starter.Modules.Sms;\n');
  write(starter, 'frontend/starter-web/src/app/features/ops/ops-extra.routes.ts', 'export const OPS_EXTRA_ROUTES = [];\n');
  write(starter, ROUTES_FILE, read(starter, ROUTES_FILE)
    .replace(ROUTES_CLOSE, `  // codelab:module-begin Ops\n  ...OPS_EXTRA_ROUTES,\n  // codelab:module-end Ops\n${ROUTES_CLOSE}// Upstream routes note.\n`));
  const en = readJson(starter, EN_FILE);
  write(starter, EN_FILE, `${JSON.stringify({ ...en, a: { ...en.a, subtitle: 'Upstream subtitle' }, ops: { ...en.ops, extra: 'Extra' } }, null, 2)}\n`);
  const smsLine = '    <Project Path="backend/src/Modules/Sms/Starter.Modules.Sms.csproj" />';
  write(starter, SOLUTION, read(starter, SOLUTION).replace(smsLine, `${smsLine}\r\n    <Project Path="backend/tests/Modules/Starter.Modules.Sms.Tests/Starter.Modules.Sms.Tests.csproj" />`));
  write(starter, MANIFEST, `${JSON.stringify({ ...readJson(starter, MANIFEST), starterVersion: UPSTREAM_STARTER_VERSION }, null, 2)}\n`);
  return commitAll(starter, 'upstream change');
}

describe('upstream-merge exit test: a core-only fork merges a starter commit', () => {
  const temp = makeTempDir();
  const starter = join(temp.path, 'starter');
  const fork = join(temp.path, 'fork');
  const feed = createFeed(join(temp.path, 'feed-source'));
  const reportPath = join(temp.path, 'report.json');
  let starterBase;
  let initResult;

  before(() => {
    createSyntheticStarter(starter);
    starterBase = addOptionalModules(starter);
    cloneFork(starter, fork);
    initResult = runScript(join(fork, 'init.ps1'), ['-Minimal', '-ProductPrefix', 'ACME', '-RemoveModule', OPTIONAL_MODULES.join(','), '-FeedDir', feed, '-SkipNpmLock', '-SkipSecrets'], { cwd: fork });
  });
  after(() => temp.cleanup());

  it('init produced a core-only fork', () => {
    assert.equal(initResult.code, EXIT.ok, initResult.output);
    const manifest = readJson(fork, MANIFEST);
    assert.equal(manifest.starterBase, starterBase);
    assert.deepEqual([...manifest.removedModules].sort(), [...OPTIONAL_MODULES].sort());
    for (const name of OPTIONAL_MODULES) assert.equal(existsSync(join(fork, `backend/src/Modules/${name}`)), false, name);
    assert.doesNotMatch(read(fork, ROUTES_FILE), /OPS_/);
    assert.equal(readJson(fork, EN_FILE).ops, undefined);
  });

  describe('after the upstream commit', () => {
    let forkHead;
    let upstreamHead;
    let merge;
    let report;

    before(() => {
      forkHead = git(fork, ['rev-parse', 'HEAD']);
      upstreamHead = changeUpstream(starter);
      const command = `Set-Content -LiteralPath '${BUILD_MARKER}' -Value built`;
      merge = runScript(join(fork, 'tools/upstream-merge.ps1'), ['-Upstream', starter, '-BuildCommand', command, '-ReportPath', reportPath], { cwd: fork });
      report = existsSync(reportPath) ? JSON.parse(readFileSync(reportPath, 'utf8')) : null;
    });

    it('merges cleanly (exit 0, no conflicts) and runs the -BuildCommand', () => {
      assert.equal(merge.code, EXIT.ok, merge.output);
      assert.equal(report.status, 'clean');
      assert.deepEqual(report.conflicts, []);
      assert.ok(existsSync(join(fork, BUILD_MARKER)), 'the -BuildCommand ran in the fork root');
    });

    it('applies the 5 edits, the added file and the kept parts of the shared files', () => {
      for (const file of [...EDIT_FILES, ADDED_FILE, ROUTES_FILE, EN_FILE]) assert.ok(report.applied.includes(file), file);
      for (const file of EDIT_FILES) assert.match(read(fork, file), /Upstream line\.\n$/);
      assert.equal(read(fork, ADDED_FILE), '# Doc 6\n\nAdded upstream.\n');
      assert.match(read(fork, ROUTES_FILE), /\/\/ Upstream routes note\.\r?\n$/);
      assert.equal(readJson(fork, EN_FILE).a.subtitle, 'Upstream subtitle');
    });

    it('drops every change to the removed modules', () => {
      const dropped = Object.fromEntries(report.dropped.map((item) => [item.path, item.reason]));
      assert.equal(dropped['backend/src/Modules/Ops/MODULE.md'], 'removed-area');
      assert.equal(dropped['backend/src/Modules/Sms/SmsSender.cs'], 'removed-area');
      assert.equal(dropped['frontend/starter-web/src/app/features/ops/ops-extra.routes.ts'], 'removed-area');
      assert.equal(dropped[SOLUTION], 'removed-module-lines-only');
      assert.equal(dropped[MANIFEST], 'fork-manifest');
      for (const name of OPTIONAL_MODULES) assert.equal(existsSync(join(fork, `backend/src/Modules/${name}`)), false, name);
      assert.equal(existsSync(join(fork, 'frontend/starter-web/src/app/features/ops')), false);
      assert.doesNotMatch(read(fork, ROUTES_FILE), /OPS_|codelab:module(?:-begin|-end)? Ops/);
      assert.doesNotMatch(read(fork, SOLUTION), /Modules\.(Attachments|Portal|Sms|Ops)/);
      for (const language of ['en', 'ar']) assert.equal(readJson(fork, `frontend/starter-web/src/assets/i18n/${language}.json`).ops, undefined, language);
    });

    it('moves starterBase, takes the starter version and keeps the fork fields of the manifest', () => {
      const manifest = readJson(fork, MANIFEST);
      assert.equal(manifest.starterBase, upstreamHead);
      assert.equal(manifest.starterVersion, UPSTREAM_STARTER_VERSION);
      assert.equal(manifest.productPrefix, 'ACME');
      assert.deepEqual([...manifest.removedModules].sort(), [...OPTIONAL_MODULES].sort());
    });

    it('recomputes the hashes of every tracked file', () => {
      const { hashes } = readJson(fork, MANIFEST);
      const tracked = git(fork, ['ls-files']).split('\n').filter((file) => file && file !== MANIFEST && !file.startsWith('feed/'));
      assert.deepEqual(Object.keys(hashes).sort(), tracked.sort());
      for (const file of tracked) assert.equal(hashes[file], hashContent(readFileSync(join(fork, file))), file);
      assert.equal(hashes[ADDED_FILE], hashContent(readFileSync(join(fork, ADDED_FILE))));
    });

    it('stages the merge without committing it', () => {
      assert.equal(git(fork, ['rev-parse', 'HEAD']), forkHead);
      assert.equal(git(fork, ['diff', '--name-only']), '', 'everything the merge changed is staged');
      assert.ok(git(fork, ['diff', '--cached', '--name-only']).split('\n').includes(MANIFEST));
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
