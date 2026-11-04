// manifest.json: identity, SHA-256 of every file, integrity checks, and the -Force rules for a previous kit.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { jsonWithModule, makeTempDir, psQuote, runWithModule, writeFile } from './support/powershell.mjs';

const KIT_KIND = 'codelab-handover-kit';
const SCHEMA_VERSION = 1;

function sha256(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex');
}

function readManifest(kit) {
  return JSON.parse(readFileSync(join(kit, 'manifest.json'), 'utf8'));
}

function withKit(body) {
  const temp = makeTempDir();
  try {
    writeFile(temp.path, 'nuget.config', '<configuration />');
    writeFile(temp.path, 'nuget/b.pkg/1.0.0/b.pkg.1.0.0.nupkg', 'bbb');
    writeFile(temp.path, 'nuget/a.pkg/1.0.0/a.pkg.1.0.0.nupkg', 'aaa');
    writeFile(temp.path, 'OFFLINE.md', '# Offline\n');
    jsonWithModule(
      `Write-KitManifest ${psQuote(temp.path)} ([ordered]@{ projects = @('src/App.csproj') }) | Out-Null; ConvertTo-Json -InputObject 'ok'`);
    return body(temp.path);
  } finally {
    temp.cleanup();
  }
}

function integrity(kit) {
  return jsonWithModule(`ConvertTo-Json -InputObject @(Test-KitManifestIntegrity ${psQuote(kit)})`);
}

test('the manifest names the kit kind and schema and keeps the given details', () => withKit((kit) => {
  const manifest = readManifest(kit);
  assert.equal(manifest.kind, KIT_KIND);
  assert.equal(manifest.schemaVersion, SCHEMA_VERSION);
  assert.deepEqual(manifest.projects, ['src/App.csproj']);
}));

test('the manifest lists every other file once, sorted, with its size and SHA-256', () => withKit((kit) => {
  const manifest = readManifest(kit);
  const paths = manifest.files.map((entry) => entry.path);
  assert.deepEqual(paths, ['OFFLINE.md', 'nuget.config', 'nuget/a.pkg/1.0.0/a.pkg.1.0.0.nupkg', 'nuget/b.pkg/1.0.0/b.pkg.1.0.0.nupkg']);
  for (const entry of manifest.files) {
    assert.equal(entry.sha256, sha256(join(kit, entry.path)));
    assert.equal(entry.size, readFileSync(join(kit, entry.path)).length);
  }
  assert.equal(paths.includes('manifest.json'), false);
}));

test('an untouched kit has no integrity problems', () => withKit((kit) => {
  assert.deepEqual(integrity(kit), []);
}));

test('integrity reports changed, missing and unlisted files', () => withKit((kit) => {
  writeFileSync(join(kit, 'OFFLINE.md'), 'edited');
  rmSync(join(kit, 'nuget.config'));
  writeFile(kit, 'nuget/extra/1.0.0/extra.nupkg', 'x');
  const problems = integrity(kit);
  assert.ok(problems.includes('changed: OFFLINE.md'), problems.join('\n'));
  assert.ok(problems.includes('missing: nuget.config'), problems.join('\n'));
  assert.ok(problems.includes('not listed: nuget/extra/1.0.0/extra.nupkg'), problems.join('\n'));
}));

test('a manifest with another schema version is reported', () => withKit((kit) => {
  const manifest = readManifest(kit);
  writeFileSync(join(kit, 'manifest.json'), JSON.stringify({ ...manifest, schemaVersion: SCHEMA_VERSION + 1 }));
  assert.ok(integrity(kit).some((problem) => problem.startsWith('Unsupported schemaVersion')));
}));

test('a folder whose manifest has another kind is not a kit', () => withKit((kit) => {
  writeFileSync(join(kit, 'manifest.json'), JSON.stringify({ kind: 'something-else', files: [] }));
  assert.equal(jsonWithModule(`Test-KitIsKitFolder ${psQuote(kit)} | ConvertTo-Json`), false);
}));

test('-Force accepts a folder that holds only a previous kit, and clearing removes only kit entries', () => withKit((kit) => {
  const sourceRoot = makeTempDir();
  try {
    const clear = jsonWithModule(`Assert-KitOutputPath ${psQuote(kit)} ${psQuote(sourceRoot.path)} $true | ConvertTo-Json`);
    assert.equal(clear, true);
    jsonWithModule(`Clear-KitPreviousOutput ${psQuote(kit)}; ConvertTo-Json -InputObject 'ok'`);
    for (const name of ['manifest.json', 'nuget.config', 'nuget', 'OFFLINE.md']) assert.equal(existsSync(join(kit, name)), false, name);
    assert.equal(existsSync(kit), true);
  } finally {
    sourceRoot.cleanup();
  }
}));

test('an empty or missing output folder needs no clearing', () => {
  const temp = makeTempDir();
  const sourceRoot = makeTempDir();
  try {
    assert.equal(jsonWithModule(`Assert-KitOutputPath ${psQuote(temp.path)} ${psQuote(sourceRoot.path)} $false | ConvertTo-Json`), false);
    assert.equal(
      jsonWithModule(`Assert-KitOutputPath ${psQuote(join(temp.path, 'new'))} ${psQuote(sourceRoot.path)} $false | ConvertTo-Json`), false);
  } finally {
    temp.cleanup();
    sourceRoot.cleanup();
  }
});

test('a drive root is never accepted as OutputPath', () => {
  const sourceRoot = makeTempDir();
  try {
    const result = runWithModule(`Assert-KitOutputPath 'C:\\' ${psQuote(sourceRoot.path)} $true`);
    assert.notEqual(result.code, 0);
    assert.match(result.output, /is a drive root/);
  } finally {
    sourceRoot.cleanup();
  }
});
