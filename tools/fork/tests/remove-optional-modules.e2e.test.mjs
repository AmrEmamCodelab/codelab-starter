// End-to-end tests of removeOptionalModules (init Step 2) on a synthetic starter that carries the optional modules'
// anatomy (backend modules, services, deploy-kit scripts, marked registration lines in .ts, .cs and .psm1 files,
// translation keys, a root npm workspace), plus the tools/remove-module.ps1 -Optional entry point.
// Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { after, before, beforeEach, describe, it } from 'node:test';
import { findRemnants } from '../lib/deletability.mjs';
import { readManifest } from '../lib/manifest.mjs';
import { findUnmarkedAnchors, removeNpmWorkspaces, removeOptionalModules, runOptionalRemovalCli } from '../lib/remove-module.mjs';
import { OPTIONAL_MODULE_IDS, optionalModuleOwnedPaths } from '../lib/modules.mjs';
import { RefusalError } from '../lib/repo.mjs';
import { makeTempDir, runScript } from './support/powershell.mjs';
import { createSyntheticStarter, read, REPO_ROOT, write } from './support/synthetic-starter.mjs';

const EXIT = Object.freeze({ ok: 0, failed: 1, usage: 2 });
const SOLUTION = 'codelab-starter.slnx';
const HOST = 'backend/src/Starter.Api/Starter.Api.csproj';
const ROUTES = 'frontend/starter-web/src/app/app.routes.ts';
const APP_SPEC = 'frontend/starter-web/src/app/app.spec.ts';
const ALLOWLIST = 'backend/tests/Starter.Contract.Tests/EndpointAllowlist.cs';
const COMMON = 'deploy/kits/Install-Common.psm1';
const NPM_MANIFEST = 'package.json';
const OPS_WORKSPACE = 'services/ops-console/web/ops-web';

const OPTIONAL_SOLUTION_LINES = `  <Folder Name="/optional/">
    <Project Path="backend/src/Modules/Attachments/Starter.Modules.Attachments.csproj" />
    <Project Path="backend/src/Modules/Ops/Starter.Modules.Ops.csproj" />
    <Project Path="backend/tests/Modules/Starter.Modules.Ops.Tests/Starter.Modules.Ops.Tests.csproj" />
  </Folder>
  <Folder Name="/services/attachment-service/">
    <Project Path="services/attachment-service/src/Codelab.Attachments.Api/Codelab.Attachments.Api.csproj" />
  </Folder>
  <Folder Name="/services/ops-console/">
    <Project Path="services/ops-console/src/Codelab.OpsConsole.Api/Codelab.OpsConsole.Api.csproj" />
  </Folder>
</Solution>`;

const MARKED_ROUTES = `import type { Routes } from '@angular/router';
import { A_ROUTES } from './features/a/a.routes';
import { B_ROUTES } from './features/b/b.routes'; // codelab:module B
import { ATTACHMENTS_ROUTES } from './features/attachments/attachments.routes'; // codelab:module Attachments

export const routes: Routes = [
  ...A_ROUTES,
  ...ATTACHMENTS_ROUTES, // codelab:module Attachments
  // codelab:module-begin B
  ...B_ROUTES,
  // codelab:module-end B
];
`;
const ROUTES_WITHOUT_ATTACHMENTS = MARKED_ROUTES.split('\n').filter((line) => !line.includes('Attachments')).join('\n');

const ALLOWLIST_TEXT = `namespace Starter.Contract.Tests;

public static class EndpointAllowlist
{
    public const string OpsModule = "Ops"; // codelab:module Ops
    // codelab:module-begin Ops
    public const string OpsHealth = "GET /api/ops/health";
    // codelab:module-end Ops
    public const string Kept = "kept";
}
`;
const ALLOWLIST_AFTER = `namespace Starter.Contract.Tests;

public static class EndpointAllowlist
{
    public const string Kept = "kept";
}
`;
const COMMON_TEXT = 'function Install-Common { }\r\n# codelab:module-begin Ops\r\nfunction Install-OpsHelper { }\r\n# codelab:module-end Ops\r\nExport-ModuleMember -Function * # keep\r\n';
const COMMON_AFTER = 'function Install-Common { }\r\nExport-ModuleMember -Function * # keep\r\n';

/** Joins path segments with backslashes, the separator the host's project references use. */
const windowsPath = (...segments) => segments.join('\\');

/** Adds the optional modules' anatomy to a synthetic starter (no git metadata: removal never needs it). */
function addOptionalAnatomy(root) {
  write(root, SOLUTION, read(root, SOLUTION).replace('</Solution>', OPTIONAL_SOLUTION_LINES));
  const attachmentsReference = windowsPath('..', 'Modules', 'Attachments', 'Starter.Modules.Attachments.csproj');
  const opsReference = windowsPath('..', 'Modules', 'Ops', 'Starter.Modules.Ops.csproj');
  write(root, HOST, read(root, HOST).replace('  </ItemGroup>', `    <ProjectReference Include="${attachmentsReference}" />\r\n    <ProjectReference Include="${opsReference}" /> <!-- codelab:module Ops -->\r\n  </ItemGroup>`));
  for (const name of ['Attachments', 'Ops']) {
    write(root, `backend/src/Modules/${name}/Starter.Modules.${name}.csproj`, '<Project Sdk="Microsoft.NET.Sdk" />\r\n');
  }
  write(root, 'backend/tests/Modules/Starter.Modules.Ops.Tests/Starter.Modules.Ops.Tests.csproj', '<Project Sdk="Microsoft.NET.Sdk" />\r\n');
  write(root, 'frontend/starter-web/src/app/features/attachments/attachments.routes.ts', 'export const ATTACHMENTS_ROUTES = [];\n');
  write(root, 'services/attachment-service/src/Codelab.Attachments.Api/Codelab.Attachments.Api.csproj', '<Project Sdk="Microsoft.NET.Sdk.Web" />\r\n');
  write(root, 'services/ops-console/src/Codelab.OpsConsole.Api/Codelab.OpsConsole.Api.csproj', '<Project Sdk="Microsoft.NET.Sdk.Web" />\r\n');
  write(root, `${OPS_WORKSPACE}/package.json`, '{ "name": "ops-web" }\n');
  write(root, 'deploy/kits/Install-Attachment.ps1', '# attachment kit\r\n');
  write(root, 'deploy/kits/Install-Ops.ps1', '# ops kit\r\n');
  write(root, 'deploy/kits/Install-Api.ps1', '# api kit stays\r\n');
  write(root, COMMON, COMMON_TEXT);
  write(root, ALLOWLIST, ALLOWLIST_TEXT);
  write(root, ROUTES, MARKED_ROUTES);
  write(root, APP_SPEC, "import { ATTACHMENTS_DEMO_ROUTE_PATH } from './features/attachments/attachments.routes'; // codelab:module Attachments\nexport const kept = 1;\n");
  const npm = JSON.parse(read(root, NPM_MANIFEST));
  npm.workspaces = [...npm.workspaces, OPS_WORKSPACE];
  write(root, NPM_MANIFEST, `${JSON.stringify(npm, null, 2)}\n`);
  for (const language of ['en', 'ar']) {
    const file = `frontend/starter-web/src/assets/i18n/${language}.json`;
    const keys = JSON.parse(read(root, file));
    write(root, file, `${JSON.stringify({ ...keys, attachments: { title: 'files' }, ops: { title: 'ops' } }, null, 2)}\n`);
  }
}

/** A digest of every file under root (path and content), to prove that nothing changed. */
function treeDigest(root) {
  const hash = createHash('sha256');
  const visit = (dir) => {
    for (const entry of readdirSync(dir).sort()) {
      const full = join(dir, entry);
      if (statSync(full).isDirectory()) visit(full);
      else hash.update(full).update(readFileSync(full));
    }
  };
  visit(root);
  return hash.digest('hex');
}

function captureIo() {
  const lines = { log: [], error: [] };
  return { io: { log: (line) => lines.log.push(line), error: (line) => lines.error.push(line) }, lines };
}

describe('removeNpmWorkspaces', () => {
  it('drops the listed workspaces and keeps the rest of the file', () => {
    const text = `${JSON.stringify({ name: 'x', workspaces: ['a', OPS_WORKSPACE, 'b'] }, null, 2)}\n`;
    assert.equal(removeNpmWorkspaces(text, [OPS_WORKSPACE]), `${JSON.stringify({ name: 'x', workspaces: ['a', 'b'] }, null, 2)}\n`);
  });

  it('keeps a BOM and CRLF line endings', () => {
    const text = `﻿${JSON.stringify({ workspaces: [OPS_WORKSPACE] }, null, 2).split('\n').join('\r\n')}\r\n`;
    assert.equal(removeNpmWorkspaces(text, [OPS_WORKSPACE]), '﻿{\r\n  "workspaces": []\r\n}\r\n');
  });

  it('returns null when there is nothing to drop', () => {
    assert.equal(removeNpmWorkspaces('{ "workspaces": ["a"] }', [OPS_WORKSPACE]), null);
    assert.equal(removeNpmWorkspaces('{ "name": "x" }', [OPS_WORKSPACE]), null);
    assert.equal(removeNpmWorkspaces('not json', []), null);
  });

  it('refuses a manifest that is not JSON', () => {
    assert.throws(() => removeNpmWorkspaces('not json', [OPS_WORKSPACE]), RefusalError);
  });
});

describe('removeOptionalModules on a synthetic starter', () => {
  const temp = makeTempDir();
  const root = join(temp.path, 'starter');
  before(() => {
    createSyntheticStarter(root);
    rmSync(join(root, '.git'), { recursive: true, force: true });
    addOptionalAnatomy(root);
  });
  after(() => temp.cleanup());

  it('refuses an unknown id with nothing changed', () => {
    const digest = treeDigest(root);
    assert.throws(() => removeOptionalModules(root, ['identity']), (error) => error instanceof RefusalError && /'identity' is not an optional module id/.test(error.message));
    assert.equal(treeDigest(root), digest);
  });

  it('refuses while a marker anchor lacks the markers, naming the file, with nothing changed', () => {
    write(root, APP_SPEC, 'export const kept = 1;\n');
    const digest = treeDigest(root);
    assert.deepEqual(findUnmarkedAnchors(root, ['attachments'], []), [`${APP_SPEC} (codelab:module Attachments)`]);
    assert.throws(() => removeOptionalModules(root, ['attachments', 'ops']), (error) => error instanceof RefusalError && error.message.includes(APP_SPEC));
    assert.equal(treeDigest(root), digest);
    write(root, APP_SPEC, "import { X } from './features/attachments/attachments.routes'; // codelab:module Attachments\nexport const kept = 1;\n");
  });

  it('plans with whatIf: lists what it would remove and changes nothing', () => {
    const digest = treeDigest(root);
    const { io, lines } = captureIo();
    const { removed } = removeOptionalModules(root, ['ops', 'attachments'], { whatIf: true, io });
    assert.deepEqual(removed, [
      'backend/src/Modules/Attachments/',
      'frontend/starter-web/src/app/features/attachments/',
      'services/attachment-service/',
      'deploy/kits/Install-Attachment.ps1',
      'backend/src/Modules/Ops/',
      'backend/tests/Modules/Starter.Modules.Ops.Tests/',
      'services/ops-console/',
      'deploy/kits/Install-Ops.ps1',
    ]);
    assert.ok(lines.log.includes(`  would edit ${COMMON}`), lines.log.join('\n'));
    assert.ok(lines.log.includes(`  would edit ${NPM_MANIFEST}`));
    assert.equal(treeDigest(root), digest);
  });

  it('removes attachments and ops at file level and leaves the other modules alone', () => {
    const { io, lines } = captureIo();
    const { removed } = removeOptionalModules(root, ['attachments', 'ops'], { io });

    for (const owned of removed) assert.ok(!existsSync(join(root, owned)), `${owned} still exists`);
    assert.ok(existsSync(join(root, 'deploy/kits/Install-Api.ps1')));
    assert.ok(existsSync(join(root, 'backend/src/Modules/B/Starter.Modules.B.csproj')));

    const solution = read(root, SOLUTION);
    assert.doesNotMatch(solution, /Attachments|Ops|attachment-service|ops-console|\/optional\//);
    assert.match(solution, /Starter\.Modules\.B\.csproj/);
    assert.doesNotMatch(read(root, HOST), /Attachments|Modules\\Ops/);
    assert.equal(read(root, ROUTES), ROUTES_WITHOUT_ATTACHMENTS);
    assert.equal(read(root, APP_SPEC), 'export const kept = 1;\n');
    assert.equal(read(root, ALLOWLIST), ALLOWLIST_AFTER);
    assert.equal(read(root, COMMON), COMMON_AFTER);
    assert.deepEqual(JSON.parse(read(root, NPM_MANIFEST)).workspaces, ['packages/npm/*', 'frontend/starter-web']);
    const en = JSON.parse(read(root, 'frontend/starter-web/src/assets/i18n/en.json'));
    assert.deepEqual(Object.keys(en), ['a', 'b']);
    assert.deepEqual(readManifest(root).removedModules, ['Attachments', 'Ops']);
    for (const name of ['Attachments', 'Ops']) assert.deepEqual(findRemnants(root, name), [], name);
    assert.match(lines.log.join('\n'), /Optional module\(s\) removed: attachments, ops\./);
  });

  it('is a no-op the second time', () => {
    const digest = treeDigest(root);
    assert.deepEqual(removeOptionalModules(root, ['ops', 'attachments']), { removed: [] });
    assert.equal(treeDigest(root), digest);
  });
});

describe('runOptionalRemovalCli and tools/remove-module.ps1 -Optional', () => {
  const temp = makeTempDir();
  const root = join(temp.path, 'starter');
  before(() => {
    createSyntheticStarter(root);
    rmSync(join(root, '.git'), { recursive: true, force: true });
    addOptionalAnatomy(root);
  });
  beforeEach(() => write(root, APP_SPEC, "import { X } from './a'; // codelab:module Attachments\n"));
  after(() => temp.cleanup());

  it('maps usage errors to 2 and refusals to 1', () => {
    assert.equal(runOptionalRemovalCli(['--root', root], captureIo().io), EXIT.usage);
    assert.equal(runOptionalRemovalCli(['--bogus'], captureIo().io), EXIT.usage);
    const refused = captureIo();
    assert.equal(runOptionalRemovalCli(['--root', root, '--ids', 'nope'], refused.io), EXIT.failed);
    assert.match(refused.lines.error.join('\n'), /'nope' is not an optional module id/);
  });

  it('plans through the PowerShell entry point with -WhatIf and changes nothing', () => {
    const digest = treeDigest(root);
    const result = runScript(join(root, 'tools/remove-module.ps1'), ['-Optional', 'ops', '-WhatIf']);
    assert.equal(result.code, EXIT.ok, result.output);
    assert.match(result.output, /would delete services\/ops-console\//);
    assert.equal(treeDigest(root), digest);
  });

  it('refuses an unknown id (1) and -Name with -Optional (2) through the PowerShell entry point', () => {
    assert.equal(runScript(join(root, 'tools/remove-module.ps1'), ['-Optional', 'nope']).code, EXIT.failed);
    assert.equal(runScript(join(root, 'tools/remove-module.ps1'), ['-Optional', 'ops', '-Name', 'B']).code, EXIT.usage);
  });

  it('removes through the PowerShell entry point', () => {
    const result = runScript(join(root, 'tools/remove-module.ps1'), ['-Optional', 'ops']);
    assert.equal(result.code, EXIT.ok, result.output);
    assert.ok(!existsSync(join(root, 'services/ops-console')));
    assert.deepEqual(readManifest(root).removedModules, ['Ops']);
  });
});

describe('the starter tree itself', () => {
  // Regression: the core-only preset (every optional id at once) was refused because shared files lacked the markers.
  it('carries every marker anchor of every optional id, so the core-only removal is not refused', () => {
    const ownedPaths = OPTIONAL_MODULE_IDS.flatMap(optionalModuleOwnedPaths);
    assert.deepEqual(findUnmarkedAnchors(REPO_ROOT, OPTIONAL_MODULE_IDS, ownedPaths), []);
  });
});
