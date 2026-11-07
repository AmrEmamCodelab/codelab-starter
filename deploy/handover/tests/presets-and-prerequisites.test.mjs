// P10: the Full preset (whole-starter closure), lock-file coverage, and the listed-not-shipped prerequisites catalog.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { EXIT, HANDOVER_DIR, NEW_KIT_SCRIPT, jsonWithModule, makeTempDir, psQuote, runScript, runWithModule, writeFile } from './support/powershell.mjs';
import { createSourceTree } from './support/fixtures.mjs';

const REPO_ROOT = resolve(HANDOVER_DIR, '..', '..');
const CATALOG_PATH = join(HANDOVER_DIR, 'prerequisites.json');
const SHA256 = /^[0-9a-f]{64}$/;
const FULL = 'Full';

function withTemp(body) {
  const temp = makeTempDir();
  try {
    return body(temp.path);
  } finally {
    temp.cleanup();
  }
}

function catalog() {
  return JSON.parse(readFileSync(CATALOG_PATH, 'utf8'));
}

function pinProblems(catalogJson, root, lockRoots = []) {
  const roots = lockRoots.map(psQuote).join(', ');
  return jsonWithModule(
    `$c = ConvertFrom-Json ${psQuote(JSON.stringify(catalogJson))}; ConvertTo-Json -InputObject @(Get-KitPrerequisitePinProblems $c ${psQuote(root)} @(${roots}))`);
}

function shapeProblems(catalogJson) {
  return jsonWithModule(
    `$c = ConvertFrom-Json ${psQuote(JSON.stringify(catalogJson))}; ConvertTo-Json -InputObject @(Get-KitPrerequisitesShapeProblems $c)`);
}

// --- Full preset -------------------------------------------------------------------------------------------------

test('every Full preset entry exists in this repository and is a dotnet solution or an npm project', () => {
  const preset = jsonWithModule(`Resolve-KitPreset ${psQuote(REPO_ROOT)} ${psQuote(FULL)} | ConvertTo-Json -Depth 4`);
  assert.equal(preset.Name, FULL);
  assert.deepEqual(preset.Skipped ?? [], []);
  assert.ok(preset.Projects.includes('codelab-starter.slnx'));
  for (const service of ['attachment-service', 'integration-gateway', 'ops-console']) {
    assert.ok(preset.Projects.some((p) => p.startsWith(`services\\${service}\\`)), service);
  }
  for (const app of ['frontend\\starter-web', 'services\\ops-console\\web\\ops-web']) {
    assert.ok(preset.NpmProjects.includes(app), app);
  }
});

test('the Full preset builds the Codelab npm packages before the apps that import their dist output', () => {
  const preset = jsonWithModule(`Resolve-KitPreset ${psQuote(REPO_ROOT)} ${psQuote(FULL)} | ConvertTo-Json -Depth 4`);
  const firstApp = preset.NpmProjects.indexOf('frontend\\starter-web');
  const lastPackage = Math.max(...preset.NpmProjects.map((p, i) => (p.startsWith('packages\\npm\\') ? i : -1)));
  assert.ok(lastPackage >= 0 && lastPackage < firstApp, preset.NpmProjects.join(', '));
});

test('the Full preset covers every package-lock.json in this repository', () => {
  const uncovered = jsonWithModule([
    `$root = ${psQuote(REPO_ROOT)}; $p = Resolve-KitPreset $root ${psQuote(FULL)};`,
    '$npm = @(Resolve-KitNpmProjects $root $p.NpmProjects);',
    'ConvertTo-Json -InputObject @(Get-KitUncoveredLockFiles @(Get-KitSourceFiles $root) @(Get-KitNpmInstallRoots $npm))',
  ].join(' '));
  assert.deepEqual(uncovered, []);
});

test('preset entries missing in a fork are skipped and reported, not silently dropped', () => withTemp((root) => {
  createSourceTree(root);
  writeFile(root, 'codelab-starter.slnx', '<Solution />');
  writeFile(root, 'frontend/starter-web/package.json', '{}');
  const preset = jsonWithModule(`Resolve-KitPreset ${psQuote(root)} 'full' | ConvertTo-Json -Depth 4`);
  assert.equal(preset.Name, FULL, 'the preset name is case-insensitive');
  assert.deepEqual(preset.Projects, ['codelab-starter.slnx']);
  assert.deepEqual(preset.NpmProjects, ['frontend\\starter-web']);
  assert.ok(preset.Skipped.includes('services/ops-console/Codelab.OpsConsole.slnx'), preset.Skipped.join(', '));
  assert.ok(preset.Skipped.includes('packages/npm/ng-dga'), preset.Skipped.join(', '));
}));

test('an unknown preset is a usage error that names the known presets', () => withTemp((root) => {
  const result = runWithModule(`Resolve-KitPreset ${psQuote(root)} 'Everything'`);
  assert.notEqual(result.code, EXIT.success);
  assert.match(result.output, /Unknown preset 'Everything' \(known: Full\)/);
}));

test('New-HandoverKit.ps1 refuses an unknown preset with exit 2 before any restore', () => withTemp((root) => {
  createSourceTree(root);
  const out = makeTempDir();
  try {
    const result = runScript(NEW_KIT_SCRIPT, ['-SourceRoot', root, '-OutputPath', join(out.path, 'kit'), '-Preset', 'Nope']);
    assert.equal(result.code, EXIT.usage, result.output);
    assert.equal(existsSync(join(out.path, 'kit')), false);
  } finally {
    out.cleanup();
  }
}));

test('preset entries come first and explicit duplicates are dropped case-insensitively', () => {
  const joined = jsonWithModule(
    `ConvertTo-Json -InputObject @(Join-KitEntryLists @('a\\b.slnx', 'c') @('A/B.slnx', 'd', '', 'c/'))`);
  assert.deepEqual(joined, ['a\\b.slnx', 'c', 'd']);
});

test('lock files outside every install root are reported', () => {
  const files = ['package-lock.json', 'apps/portal/package-lock.json', 'tools/x/package-lock.json', 'apps/portal/package.json'];
  const uncovered = jsonWithModule(
    `ConvertTo-Json -InputObject @(Get-KitUncoveredLockFiles @(${files.map(psQuote).join(', ')}) @('', 'apps\\portal'))`);
  assert.deepEqual(uncovered, ['tools/x/package-lock.json']);
});

// --- Prerequisites catalog ---------------------------------------------------------------------------------------

test('the catalog records the listed decision with an exact version and SHA-256 per installer', () => {
  const data = catalog();
  assert.equal(data.decision, 'listed');
  assert.deepEqual(shapeProblems(data), []);
  const ids = data.prerequisites.map((p) => p.id);
  assert.deepEqual(ids, ['dotnet-sdk', 'node', 'playwright-browsers']);
  for (const item of data.prerequisites) {
    assert.match(item.version, /^\d+\.\d+\.\d+$/, item.id);
    for (const file of item.files) {
      assert.match(file.sha256, SHA256, file.fileName);
      assert.ok(file.url.startsWith('https://') && file.url.endsWith(file.fileName), file.fileName);
      assert.ok(file.fileName.includes(item.version), file.fileName);
    }
  }
});

test('the catalog matches this repository pins (global.json, .nvmrc, locked playwright-core)', () => {
  assert.deepEqual(pinProblems(catalog(), REPO_ROOT, [REPO_ROOT]), []);
});

test('shape problems are reported: schema, decision, hash, URL and missing version', () => {
  const bad = {
    schemaVersion: 2,
    decision: 'shipped',
    prerequisites: [
      { id: 'x', files: [{ fileName: 'x.exe', url: 'http://example.test/x.exe', sha256: 'ABC' }] },
      { version: '1.0.0' },
    ],
  };
  const problems = shapeProblems(bad).join('\n');
  for (const fragment of ['schemaVersion must be 1', "decision must be 'listed'", 'x has no version',
    'x.exe has no lower-case SHA-256', 'x.exe has no https URL', 'an item has no id']) {
    assert.ok(problems.includes(fragment), `${fragment} in\n${problems}`);
  }
});

test('an upper-case SHA-256 is refused (the catalog stores the lower-case hex Get-FileHash comparisons use)', () => {
  const data = catalog();
  data.prerequisites[0].files[0].sha256 = data.prerequisites[0].files[0].sha256.toUpperCase();
  assert.equal(shapeProblems(data).length, 1);
});

test('drift from global.json, .nvmrc and the lock file is reported', () => withTemp((root) => {
  createSourceTree(root);
  writeFile(root, 'package-lock.json', JSON.stringify({
    packages: { '': {}, 'node_modules/playwright-core': { version: '1.0.0' } },
  }));
  writeFile(root, 'global.json', JSON.stringify({ sdk: { version: '10.0.999' } }));
  writeFile(root, '.nvmrc', '22\n');
  const problems = pinProblems(catalog(), root, [root]).join('\n');
  assert.match(problems, /dotnet-sdk 10\.0\.403 differs from global\.json 10\.0\.999/);
  assert.match(problems, /node 24\.22\.2 does not satisfy \.nvmrc 22/);
  assert.match(problems, /playwright-browsers 1\.64\.1 differs from playwright-core 1\.0\.0 in \./);
}));

test('a Node pin matches by major, by full version, and with a v prefix', () => {
  const cases = [['24.22.2', '24', true], ['24.22.2', 'v24.22.2', true], ['24.22.2', '24.22', true], ['24.22.2', '2', false], ['24.22.2', '24.2', false]];
  for (const [version, pin, expected] of cases) {
    assert.equal(jsonWithModule(`Test-KitNodeVersionMatch ${psQuote(version)} ${psQuote(pin)} | ConvertTo-Json`), expected, `${version} vs ${pin}`);
  }
});

test('installed tools that differ from the listed installers produce warnings', () => {
  const run = (dotnet, node) => jsonWithModule(
    `ConvertTo-Json -InputObject @(Get-KitInstalledPrerequisiteWarnings (Read-KitPrerequisites) ${dotnet === null ? '$null' : psQuote(dotnet)} ${node === null ? '$null' : psQuote(node)})`);
  assert.deepEqual(run('10.0.403', 'v24.22.2'), []);
  const warnings = run('10.0.500', null).join('\n');
  assert.match(warnings, /\.NET SDK 10\.0\.403 is listed; this machine has 10\.0\.500\./);
  assert.match(warnings, /Node\.js 24\.22\.2 is not installed\./);
});

test('a missing catalog is a usage error', () => withTemp((root) => {
  const result = runWithModule(`Read-KitPrerequisites ${psQuote(join(root, 'none.json'))}`);
  assert.notEqual(result.code, EXIT.success);
  assert.match(result.output, /Prerequisites catalog .* not found/);
}));

test('BUILD-OFFLINE.md is a short step list naming the offline commands and the hash check', () => {
  const text = readFileSync(join(HANDOVER_DIR, 'BUILD-OFFLINE.md'), 'utf8');
  for (const fragment of ['Get-FileHash', 'npm ci --offline --cache', 'Test-HandoverKit.ps1', 'dotnet restore', 'listed, not shipped']) {
    assert.ok(text.includes(fragment), fragment);
  }
  assert.ok(text.split('\n').length < 60, 'BUILD-OFFLINE.md stays short');
});

test('kit entries include the copied catalog and BUILD-OFFLINE.md, so -Force can replace them', () => {
  const layout = jsonWithModule('Get-KitLayout | ConvertTo-Json');
  assert.equal(layout.PrerequisitesDataFile, 'prerequisites.json');
  assert.equal(layout.BuildOfflineFile, 'BUILD-OFFLINE.md');
  assert.ok(existsSync(layout.BuildOfflineSourceFile));
});

// --- Source file list ----------------------------------------------------------------------------------------------

test('a git-listed source folder named bin is kept (ng-tools src/bin), ignored build output is not', () => withTemp((root) => {
  const git = (args) => spawnSync('git', ['-C', root, ...args], { encoding: 'utf8' });
  assert.equal(git(['init', '-q']).status, 0);
  writeFile(root, '.gitignore', 'out/\n');
  writeFile(root, 'src/bin/cli.mjs', 'export {};');
  writeFile(root, 'out/bin/app.dll', 'x');
  const files = jsonWithModule(`ConvertTo-Json -InputObject @(Get-KitSourceFiles ${psQuote(root)})`);
  assert.ok(files.includes('src/bin/cli.mjs'), files.join(', '));
  assert.ok(!files.some((f) => f.startsWith('out/')), files.join(', '));
}));

test('this repository lists the ng-tools CLI entry points as source files', () => {
  const files = jsonWithModule(`ConvertTo-Json -InputObject @(Get-KitSourceFiles ${psQuote(REPO_ROOT)} | Where-Object { $_ -like 'packages/npm/ng-tools/src/bin/*' })`);
  assert.ok(files.includes('packages/npm/ng-tools/src/bin/codelab-ng-tools.mjs'), String(files));
});

test('bins are re-linked offline without running install scripts', () => {
  const args = jsonWithModule(`ConvertTo-Json -InputObject @(Get-KitNpmRelinkArguments 'C:\cache')`);
  assert.deepEqual(args.slice(0, 5), ['rebuild', '--ignore-scripts', '--offline', '--cache', 'C:\cache']);
});
