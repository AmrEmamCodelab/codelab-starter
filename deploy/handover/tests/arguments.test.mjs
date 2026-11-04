// Argument validation of New-HandoverKit.ps1 and Test-HandoverKit.ps1: every refusal exits 2 before any restore,
// and a refused -Force never deletes anything.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { EXIT, NEW_KIT_SCRIPT, TEST_KIT_SCRIPT, makeTempDir, runScript, writeFile } from './support/powershell.mjs';
import { PROJECT, createSourceTree } from './support/fixtures.mjs';

function withTrees(body) {
  const source = makeTempDir();
  const outside = makeTempDir();
  try {
    createSourceTree(source.path);
    return body({ root: source.path, outside: outside.path });
  } finally {
    source.cleanup();
    outside.cleanup();
  }
}

function newKit(root, args) {
  return runScript(NEW_KIT_SCRIPT, ['-SourceRoot', root, ...args]);
}

function assertUsage(result, fragment) {
  assert.equal(result.code, EXIT.usage, result.output);
  assert.match(result.output, /USAGE:/);
  assert.ok(result.output.includes(fragment), `expected "${fragment}" in:\n${result.output}`);
}

test('OutputPath is required', () => withTrees(({ root }) => {
  assertUsage(newKit(root, ['-Projects', PROJECT]), 'OutputPath is required');
}));

test('at least one project or npm project is required', () => withTrees(({ root, outside }) => {
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit')]), 'at least one');
}));

test('a missing project is refused', () => withTrees(({ root, outside }) => {
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-Projects', 'src/Nope/Nope.csproj']), 'Project not found');
}));

test('a file that is not a project or solution is refused', () => withTrees(({ root, outside }) => {
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-Projects', 'global.json']), 'is not a project or solution');
}));

test('comma-separated project lists are split and each entry is checked', () => withTrees(({ root, outside }) => {
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-Projects', `${PROJECT},src/Missing.csproj`]), 'src/Missing.csproj');
}));

test('an npm folder without package.json is refused', () => withTrees(({ root, outside }) => {
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-NpmProjects', 'src']), 'has no package.json');
}));

test('an npm folder that is neither locked nor a workspace member is refused', () => withTrees(({ root, outside }) => {
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-NpmProjects', 'tools/loose']), 'is not a workspace of');
}));

test('a feed without a nuget folder is refused for NuGet projects', () => withTrees(({ root, outside }) => {
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-Projects', PROJECT, '-FeedPath', 'nofeed']), 'has no nuget folder');
}));

test('an OutputPath inside the source root is refused', () => withTrees(({ root }) => {
  assertUsage(newKit(root, ['-OutputPath', join(root, 'kit'), '-Projects', PROJECT]), 'must not be inside the source root');
}));

test('an OutputPath that contains the source root is refused', () => withTrees(({ root }) => {
  assertUsage(newKit(root, ['-OutputPath', join(root, '..'), '-Projects', PROJECT]), 'must not be inside the source root or contain it');
}));

test('a non-empty OutputPath is refused without -Force and left untouched', () => withTrees(({ root, outside }) => {
  const keep = writeFile(outside, 'kit/notes.txt', 'mine');
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-Projects', PROJECT]), 'pass -Force');
  assert.ok(existsSync(keep));
}));

test('-Force refuses a non-empty folder that holds no kit manifest and deletes nothing', () => withTrees(({ root, outside }) => {
  const keep = writeFile(outside, 'kit/notes.txt', 'mine');
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-Projects', PROJECT, '-Force']), 'holds no handover kit manifest');
  assert.ok(existsSync(keep));
}));

test('-Force refuses a previous kit that also holds foreign entries and deletes nothing', () => withTrees(({ root, outside }) => {
  const manifest = writeFile(outside, 'kit/manifest.json', JSON.stringify({ kind: 'codelab-handover-kit', schemaVersion: 1, files: [] }));
  const keep = writeFile(outside, 'kit/notes.txt', 'mine');
  mkdirSync(join(outside, 'kit', 'nuget'));
  assertUsage(newKit(root, ['-OutputPath', join(outside, 'kit'), '-Projects', PROJECT, '-Force']), 'notes.txt');
  assert.ok(existsSync(keep));
  assert.ok(existsSync(manifest));
}));

test('Test-HandoverKit requires KitPath', () => {
  const result = runScript(TEST_KIT_SCRIPT, []);
  assertUsage(result, 'KitPath is required');
});

test('Test-HandoverKit refuses a folder without a kit manifest', () => withTrees(({ outside }) => {
  assertUsage(runScript(TEST_KIT_SCRIPT, ['-KitPath', outside]), 'holds no handover kit manifest');
}));

test('Test-HandoverKit fails (exit 1) on a kit whose files do not match the manifest', () => withTrees(({ outside }) => {
  writeFile(outside, 'manifest.json', JSON.stringify({
    kind: 'codelab-handover-kit', schemaVersion: 1, sourceRoot: outside, projects: [], npmProjects: [],
    files: [{ path: 'nuget.config', size: 1, sha256: '0'.repeat(64) }],
  }));
  writeFile(outside, 'nuget.config', 'changed');
  const result = runScript(TEST_KIT_SCRIPT, ['-KitPath', outside]);
  assert.equal(result.code, EXIT.failure, result.output);
  assert.match(result.output, /INTEGRITY: changed: nuget\.config/);
}));
