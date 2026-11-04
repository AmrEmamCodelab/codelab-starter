// Opt-in end-to-end run against this repository (needs network for the first restore and a few minutes):
//   $env:CODELAB_HANDOVER_E2E = '1'; node --test "deploy/handover/tests/**/*.test.mjs"
// It builds a kit for Codelab.Security + @codelab/ng-security, proves it offline with Test-HandoverKit.ps1, then
// proves the check is real: the same kit minus one NuGet package fails offline. The positive proof on the phase 1
// exit-criterion targets (Package.Sample.Tests + ng-security-consumer) is CI step 14 of tools/build.ps1, which knows
// the package versions step 13 tested; the VM run with the network adapter disabled stays an owner step (ADR-008).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cpSync, readFileSync, rmSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { EXIT, HANDOVER_DIR, NEW_KIT_SCRIPT, TEST_KIT_SCRIPT, jsonWithModule, makeTempDir, psQuote, runScript } from './support/powershell.mjs';

const E2E_ENV_VAR = 'CODELAB_HANDOVER_E2E';
const ENABLED = process.env[E2E_ENV_VAR] === '1';
const REPO_ROOT = resolve(HANDOVER_DIR, '..', '..');
const PROJECT = 'packages/dotnet/Codelab.Security/Codelab.Security.csproj';
const NPM_PROJECT = 'packages/npm/ng-security';
const REMOVED_PACKAGE = 'microsoft.data.sqlclient';

test('a kit restores and builds offline, and a kit missing a package does not', { skip: !ENABLED && `set ${E2E_ENV_VAR}=1 to run` }, () => {
  const temp = makeTempDir();
  try {
    const kit = join(temp.path, 'kit');
    const built = runScript(NEW_KIT_SCRIPT, ['-OutputPath', kit, '-Projects', PROJECT, '-NpmProjects', NPM_PROJECT], { cwd: REPO_ROOT });
    assert.equal(built.code, EXIT.success, built.output);
    const proved = runScript(TEST_KIT_SCRIPT, ['-KitPath', kit]);
    assert.equal(proved.code, EXIT.success, proved.output);

    const broken = join(temp.path, 'broken');
    cpSync(kit, broken, { recursive: true });
    rmSync(join(broken, 'nuget', REMOVED_PACKAGE), { recursive: true, force: true });
    const manifest = JSON.parse(readFileSync(join(broken, 'manifest.json'), 'utf8'));
    jsonWithModule([
      `Write-KitManifest ${psQuote(broken)} ([ordered]@{ sourceRoot = ${psQuote(manifest.sourceRoot)};`,
      `projects = @(${psQuote(PROJECT)}); npmProjects = @(); msbuildProperties = @() }) | Out-Null;`,
      "ConvertTo-Json -InputObject 'ok'",
    ].join(' '));
    const refused = runScript(TEST_KIT_SCRIPT, ['-KitPath', broken]);
    assert.equal(refused.code, EXIT.failure, refused.output);
    assert.match(refused.output, /NU1101/);
  } finally {
    temp.cleanup();
  }
});
