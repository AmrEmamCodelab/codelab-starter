// Shape of the kit nuget.config (one folder source, <clear/>, no online source, no mapping), of the build-time
// restore config, and the trimming of the restored packages folder to a folder feed.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { jsonWithModule, makeTempDir, psQuote, writeFile } from './support/powershell.mjs';

function configXml(functionCall) {
  return jsonWithModule(`${functionCall} | ConvertTo-Json`);
}

function count(text, pattern) {
  return (text.match(pattern) ?? []).length;
}

test('the kit nuget.config clears inherited sources and lists exactly one folder source', () => {
  const xml = configXml(`New-KitNuGetConfigXml './nuget'`);
  assert.match(xml, /<packageSources>\s*<clear \/>/);
  assert.equal(count(xml, /<add /g), 1);
  assert.match(xml, /<add key="codelab-handover" value="\.\/nuget" \/>/);
});

test('the kit nuget.config has no online source and no package source mapping', () => {
  const xml = configXml(`New-KitNuGetConfigXml './nuget'`);
  assert.doesNotMatch(xml, /https?:/);
  assert.doesNotMatch(xml, /nuget\.org/);
  assert.doesNotMatch(xml, /packageSourceMapping/);
});

test('the kit nuget.config escapes XML in the source path', () => {
  const xml = configXml(`New-KitNuGetConfigXml 'C:\\kits\\a&b'`);
  assert.match(xml, /value="C:\\\\kits\\\\a&amp;b"|value="C:\\kits\\a&amp;b"/);
});

test('the restore config maps Codelab.* only to the feed and everything else to nuget.org', () => {
  const xml = configXml(`New-KitRestoreConfigXml 'C:\\feed\\nuget'`);
  assert.match(xml, /<clear \/>/);
  assert.match(xml, /<packageSource key="codelab-local">\s*<package pattern="Codelab\.\*" \/>\s*<\/packageSource>/);
  assert.match(xml, /<packageSource key="nuget\.org">\s*<package pattern="\*" \/>\s*<\/packageSource>/);
  assert.match(xml, /<add key="codelab-local" value="C:\\+feed\\+nuget" \/>/);
});

test('MSBuild properties become -p: arguments and blanks are dropped', () => {
  const args = jsonWithModule(
    `ConvertTo-Json -InputObject @(ConvertTo-KitPropertyArguments @('UseCodelabPackageSource=false', '', 'Configuration=Release'))`);
  assert.deepEqual(args, ['-p:UseCodelabPackageSource=false', '-p:Configuration=Release']);
});

test('comma-separated list arguments are split and trimmed', () => {
  const values = jsonWithModule(`ConvertTo-Json -InputObject @(Split-KitListArgument @('a.csproj, b.csproj', '', 'c.slnx'))`);
  assert.deepEqual(values, ['a.csproj', 'b.csproj', 'c.slnx']);
});

test('the packages folder is trimmed to the files a folder feed needs', () => {
  const temp = makeTempDir();
  try {
    const version = join('sample.package', '1.2.3');
    const keep = ['sample.package.1.2.3.nupkg', 'sample.package.1.2.3.nupkg.sha512', 'sample.package.nuspec', '.nupkg.metadata'];
    for (const name of keep) writeFile(temp.path, join(version, name), 'x');
    writeFile(temp.path, join(version, 'lib', 'net10.0', 'Sample.dll'), 'x');
    writeFile(temp.path, join(version, '[Content_Types].xml'), 'x');
    writeFile(temp.path, join(version, 'sample.package.1.2.3.signature.p7s'), 'x');
    jsonWithModule(`Optimize-KitNuGetFolder ${psQuote(temp.path)}; ConvertTo-Json -InputObject 'ok'`);
    assert.deepEqual(readdirSync(join(temp.path, version)).sort(), [...keep].sort());
    assert.equal(existsSync(join(temp.path, version, 'lib')), false);
  } finally {
    temp.cleanup();
  }
});
