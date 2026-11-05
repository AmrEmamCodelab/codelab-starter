// Rename replay (ADR-003): a fork initialized with renameMap Starter -> Acme (its paths, namespaces, identifiers and
// its own copy of the fork tools renamed) merges an upstream change written with Starter names. The change must land
// at the renamed paths with renamed identifiers, including a three-way merge on a renamed file the fork changed.
// Run: node --test "tools/fork/tests/**/*.test.mjs" (spawns Windows PowerShell 5.1).
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { after, before, describe, it } from 'node:test';
import { makeTempDir, runScript } from './support/powershell.mjs';
import { cloneFork, commitAll, createFeed, createSyntheticStarter, git, read, write } from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0, failed: 1 });
const RENAMED_API = 'backend/src/Acme.Api';

function changeUpstream(starter) {
  const program = 'backend/src/Starter.Api/Program.cs';
  write(starter, program, read(starter, program).replace('public static class Program\n{\n', 'public static class Program\n{\n    public const string Upstream = "STARTER_UPSTREAM";\n'));
  write(starter, 'backend/src/Starter.Api/StarterOptions.cs', [
    'namespace Starter.Api;',
    '',
    '/// <summary>Options of the Starter host (section STARTER_HOST); IStarterClock stays; Restarter does not change.</summary>',
    'public sealed class StarterOptions',
    '{',
    '    public const string SectionName = "Starter";',
    '    public string Database { get; init; } = "StarterDev";',
    '}',
    '',
  ].join('\n'));
  const module = 'backend/src/Modules/A/AModule.cs';
  write(starter, module, `${read(starter, module)}\n// Uses Starter.Modules.A.AModule and starter-web.\n`);
  return commitAll(starter, 'upstream change with starter names');
}

describe('rename replay: a Starter -> Acme fork merges upstream Starter changes', () => {
  const temp = makeTempDir();
  const starter = join(temp.path, 'starter');
  const fork = join(temp.path, 'fork');
  const feed = createFeed(join(temp.path, 'feed-source'));
  const mapFile = write(temp.path, 'rename-map.json', JSON.stringify([{ from: 'Starter', to: 'Acme' }]));
  let initResult;
  let merge;

  before(() => {
    createSyntheticStarter(starter);
    cloneFork(starter, fork);
    initResult = runScript(join(fork, 'init.ps1'), ['-Minimal', '-ProductPrefix', 'ACME', '-RemoveModule', 'B', '-FeedDir', feed, '-RenameMap', mapFile, '-SkipNpmLock', '-SkipSecrets'], { cwd: fork });
  });
  after(() => temp.cleanup());

  it('init renames paths, namespaces and identifiers, but not the manifest name', () => {
    assert.equal(initResult.code, EXIT.ok, initResult.output);
    for (const file of ['codelab-acme.slnx', `${RENAMED_API}/Acme.Api.csproj`, 'backend/src/Modules/A/Acme.Modules.A.csproj', 'frontend/acme-web/package.json', 'frontend/acme-web/public/config.json', 'codelab.starter.json']) {
      assert.ok(existsSync(join(fork, file)), file);
    }
    for (const gone of ['codelab-starter.slnx', 'backend/src/Starter.Api', 'frontend/starter-web']) assert.equal(existsSync(join(fork, gone)), false, gone);
    const program = read(fork, `${RENAMED_API}/Program.cs`);
    assert.match(program, /namespace Acme\.Api;/);
    assert.match(program, /"AcmeDev"/);
    assert.match(program, /"Restarter"/);
    assert.match(read(fork, 'backend/src/Modules/A/AModule.cs'), /namespace Acme\.Modules\.A;[\s\S]*"ACME_A"/);
    assert.equal(JSON.parse(read(fork, 'frontend/acme-web/public/config.json')).security.protocolPrefix, 'ACME');
    const manifest = JSON.parse(read(fork, 'codelab.starter.json'));
    assert.deepEqual(manifest.renameMap, [
      { from: 'Starter', to: 'Acme' },
      { from: 'codelab.starter', to: 'codelab.starter' },
      { from: 'starterBase', to: 'starterBase' },
      { from: 'starterVersion', to: 'starterVersion' },
    ]);
    assert.deepEqual(manifest.prefixTargets.map((target) => target.file), [`${RENAMED_API}/appsettings.json`, 'frontend/acme-web/public/config.json']);
    assert.match(read(fork, 'tools/fork/lib/constants.mjs'), /SOLUTION_FILE = 'codelab-acme\.slnx'/);
  });

  describe('after an upstream change written with Starter names', () => {
    before(() => {
      const program = `${RENAMED_API}/Program.cs`;
      write(fork, program, read(fork, program).replace('    public const string Restarter', '    public const string ForkOnly = "fork";\n    public const string Restarter'));
      commitAll(fork, 'fork edit away from the upstream lines');
      changeUpstream(starter);
      merge = runScript(join(fork, 'tools/upstream-merge.ps1'), ['-Upstream', starter, '-BuildCommand', 'exit 0'], { cwd: fork });
    });

    it('merges cleanly', () => {
      assert.equal(merge.code, EXIT.ok, merge.output);
    });

    it('adds the new file at the renamed path with renamed namespace and identifiers', () => {
      assert.equal(existsSync(join(fork, 'backend/src/Starter.Api')), false);
      const options = read(fork, `${RENAMED_API}/AcmeOptions.cs`);
      assert.match(options, /^namespace Acme\.Api;/);
      assert.match(options, /public sealed class AcmeOptions/);
      assert.match(options, /SectionName = "Acme"/);
      assert.match(options, /"AcmeDev"/);
      assert.match(options, /section ACME_HOST\); IAcmeClock stays; Restarter does not change/);
    });

    it('three-way merges the renamed file the fork also changed', () => {
      const program = read(fork, `${RENAMED_API}/Program.cs`);
      assert.match(program, /"ACME_UPSTREAM"/);
      assert.match(program, /ForkOnly = "fork"/);
      assert.match(read(fork, 'backend/src/Modules/A/AModule.cs'), /Uses Acme\.Modules\.A\.AModule and acme-web\./);
    });

    it('records the hashes of the renamed paths', () => {
      const manifest = JSON.parse(read(fork, 'codelab.starter.json'));
      assert.ok(manifest.hashes[`${RENAMED_API}/AcmeOptions.cs`]);
      assert.equal(manifest.hashes['backend/src/Starter.Api/StarterOptions.cs'], undefined);
      assert.equal(git(fork, ['diff', '--cached', '--name-only', '--diff-filter=U']), '');
      assert.ok(readFileSync(join(fork, 'codelab-acme.slnx'), 'utf8').includes('Acme.Modules.A'));
    });
  });
});
