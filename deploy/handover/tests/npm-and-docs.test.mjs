// npm install-root resolution, file: tarball discovery, OFFLINE.md, PREREQUISITES.md and the source zips.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { jsonWithModule, makeTempDir, psQuote, runWithModule, writeFile } from './support/powershell.mjs';
import { FEED_TARBALL, NPM_STANDALONE, NPM_WORKSPACE_MEMBER, createSourceTree } from './support/fixtures.mjs';

function withSource(body) {
  const temp = makeTempDir();
  try {
    createSourceTree(temp.path);
    return body(temp.path);
  } finally {
    temp.cleanup();
  }
}

function npmProjects(root, folders) {
  const list = folders.map(psQuote).join(', ');
  return jsonWithModule(`ConvertTo-Json -InputObject @(Resolve-KitNpmProjects ${psQuote(root)} @(${list}))`);
}

test('a workspace member installs from the workspace root', () => withSource((root) => {
  const [project] = npmProjects(root, [NPM_WORKSPACE_MEMBER]);
  assert.equal(project.Relative, NPM_WORKSPACE_MEMBER);
  assert.equal(project.InstallRoot, '');
}));

test('a project with its own lock file installs in its own folder', () => withSource((root) => {
  const [project] = npmProjects(root, [NPM_STANDALONE]);
  assert.equal(project.InstallRoot, NPM_STANDALONE);
}));

test('install roots are de-duplicated', () => withSource((root) => {
  const projects = npmProjects(root, [NPM_WORKSPACE_MEMBER, NPM_STANDALONE]);
  const roots = jsonWithModule(
    `$p = ConvertFrom-Json ${psQuote(JSON.stringify(projects))}; ConvertTo-Json -InputObject @(Get-KitNpmInstallRoots @($p))`);
  assert.deepEqual(roots, ['', NPM_STANDALONE]);
}));

test('workspace globs match one segment with * and many with **', () => {
  const cases = [
    ['packages/npm/*', 'packages/npm/widget', true],
    ['packages/npm/*', 'packages/npm/widget/inner', false],
    ['apps/**', 'apps/a/b', true],
    ['./tools/cli', 'tools/cli', true],
    ['packages/npm/*', 'packages/npmx/widget', false],
  ];
  for (const [pattern, path, expected] of cases) {
    const actual = jsonWithModule(`Test-KitWorkspaceMatch ${psQuote(pattern)} ${psQuote(path)} | ConvertTo-Json`);
    assert.equal(actual, expected, `${pattern} vs ${path}`);
  }
});

test('lock files with an empty root key are read and file: tarballs are found', () => withSource((root) => {
  const tarballs = jsonWithModule(`ConvertTo-Json -InputObject @(Get-KitNpmFileTarballs ${psQuote(root)} ${psQuote(root)})`);
  assert.deepEqual(tarballs, [FEED_TARBALL]);
}));

test('a file: tarball outside the source root is refused', () => withSource((root) => {
  writeFile(root, 'outside-lock/package-lock.json', JSON.stringify({ packages: { '': {}, 'node_modules/x': { resolved: 'file:../../elsewhere/x.tgz' } } }));
  const result = runWithModule(`Get-KitNpmFileTarballs ${psQuote(join(root, 'outside-lock'))} ${psQuote(root)}`);
  assert.notEqual(result.code, 0);
  assert.match(result.output, /outside the source root/);
}));

test('OFFLINE.md gives the offline npm command, the install folders and the file tarballs', () => {
  const projects = JSON.stringify([{ Relative: NPM_WORKSPACE_MEMBER, InstallRoot: '' }]);
  const text = jsonWithModule(
    `$p = ConvertFrom-Json ${psQuote(projects)}; New-KitOfflineText @($p) @(${psQuote(FEED_TARBALL)}) | ConvertTo-Json`);
  assert.ok(text.includes('npm ci --offline --cache ./npm-cache'));
  assert.ok(text.includes(`- \`.\` (for ${NPM_WORKSPACE_MEMBER})`));
  assert.ok(text.includes(`- \`${FEED_TARBALL}\``));
  assert.ok(text.includes('--configfile <kit>\\nuget.config'));
});

test('OFFLINE.md says so when the kit has no npm projects', () => {
  const text = jsonWithModule(`New-KitOfflineText @() @() | ConvertTo-Json`);
  assert.ok(text.includes('No npm projects are in this kit.'));
});

test('PREREQUISITES.md lists the pinned SDK and Node, Playwright and the listed installers', () => withSource((root) => {
  const text = jsonWithModule([
    `$info = [pscustomobject]@{ DotnetPin = (Get-KitDotnetSdkPin ${psQuote(root)}); DotnetUsed = '10.0.403';`,
    `NodePin = (Get-KitNodePin ${psQuote(root)}); NodeUsed = 'v24.22.2'; NpmUsed = '11.19.1';`,
    `Playwright = @(Get-KitPlaywrightVersions ${psQuote(root)}) };`,
    'New-KitPrerequisitesText $info (Read-KitPrerequisites) | ConvertTo-Json',
  ].join(' '));
  assert.ok(text.includes('| .NET SDK | 10.0.403 (global.json, rollForward latestFeature) | 10.0.403 |'), text);
  assert.ok(text.includes('| Node.js | 24 (.nvmrc) | v24.22.2 |'), text);
  assert.ok(text.includes('@playwright/test 1.58.0'), text);
  assert.ok(text.includes('## Installers (listed, not shipped)'), text);
  assert.match(text, /\| \.NET SDK \| 10\.0\.403 \| `dotnet-sdk-10\.0\.403-win-x64\.exe` \| `[0-9a-f]{64}` \| https:\/\//);
  assert.ok(text.includes('Microsoft Edge'), text);
}));

test('PREREQUISITES.md says none locked when no Playwright package is locked', () => {
  const text = jsonWithModule([
    `$info = [pscustomobject]@{ DotnetPin = [pscustomobject]@{ Version = $null; RollForward = $null }; DotnetUsed = $null;`,
    `NodePin = $null; NodeUsed = $null; NpmUsed = $null; Playwright = @() };`,
    'New-KitPrerequisitesText $info (Read-KitPrerequisites) | ConvertTo-Json',
  ].join(' '));
  assert.ok(text.includes('| Playwright | none locked | - |'), text);
  assert.ok(text.includes('| .NET SDK | not pinned'), text);
});

test('one source zip is written per package folder, with source-relative entry names and no build output', () => {
  const source = makeTempDir();
  const kit = makeTempDir();
  try {
    writeFile(source.path, 'packages/dotnet/Codelab.Alpha/Alpha.cs', 'class A {}');
    writeFile(source.path, 'packages/npm/ng-beta/package.json', '{}');
    writeFile(source.path, 'packages/vectors/canonical.vectors.json', '{}');
    const files = ['packages/dotnet/Codelab.Alpha/Alpha.cs', 'packages/npm/ng-beta/package.json', 'packages/vectors/canonical.vectors.json'];
    const listed = files.map(psQuote).join(', ');
    const result = jsonWithModule([
      `$zips = @(New-KitSourceZips ${psQuote(source.path)} ${psQuote(kit.path)} @(${listed}));`,
      `$entries = foreach ($zip in $zips) { $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path ${psQuote(kit.path)} $zip));`,
      'try { $archive.Entries | ForEach-Object FullName } finally { $archive.Dispose() } };',
      'ConvertTo-Json -InputObject ([ordered]@{ zips = $zips; entries = @($entries) })',
    ].join(' '));
    assert.deepEqual(result.zips, [
      'sources/packages/dotnet/Codelab.Alpha.zip', 'sources/packages/npm/ng-beta.zip', 'sources/packages/vectors.zip']);
    assert.deepEqual(result.entries, files);
  } finally {
    source.cleanup();
    kit.cleanup();
  }
});

test('source files exclude build output folders', () => {
  const excluded = ['a/bin/x.dll', 'a/obj/y.json', 'node_modules/z/index.js', 'pkg/dist/index.js'];
  for (const path of excluded) {
    assert.equal(jsonWithModule(`Test-KitExcludedPath ${psQuote(path)} | ConvertTo-Json`), true, path);
  }
  assert.equal(jsonWithModule(`Test-KitExcludedPath 'packages/dotnet/Codelab.Binary/Thing.cs' | ConvertTo-Json`), false);
});

test('missing package folders are reported', () => {
  const source = makeTempDir();
  try {
    writeFile(source.path, 'packages/dotnet/Codelab.Alpha/Alpha.cs', 'class A {}');
    const missing = jsonWithModule(`ConvertTo-Json -InputObject @(Get-KitMissingSourceFolders ${psQuote(source.path)})`);
    assert.deepEqual(missing, ['packages/npm', 'packages/vectors']);
  } finally {
    source.cleanup();
  }
});
