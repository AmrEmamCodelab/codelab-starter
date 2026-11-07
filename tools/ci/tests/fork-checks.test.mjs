// Run: node --test tools/ci/tests/fork-checks.test.mjs
// Contract: the phase 10 package-consumption check. An init-ed fork has no packages/, restores Codelab.* and
// @codelab/* only from feed/, a handover kit or an allowed hosted feed, and records its starterBase.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { after, describe, it } from 'node:test';
import {
  EXIT_CLEAN,
  EXIT_FINDINGS,
  EXIT_USAGE,
  FindingKind,
  checkFork,
  isAllowedSource,
  main,
  parseArguments,
  parseNugetConfig,
  patternSpecificity,
  sourcesServing,
} from '../fork-checks.mjs';

const FEED_CONFIG = `<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <!-- <add key="commented" value="https://example.invalid/v3/index.json" /> -->
  <packageSources>
    <clear />
    <add key="codelab-local" value="feed/nuget" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="codelab-local"><package pattern="Codelab.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>`;
const UNMAPPED_CONFIG = FEED_CONFIG.replace(/<packageSourceMapping>[\s\S]*<\/packageSourceMapping>/, '');
const NO_CLEAR_CONFIG = FEED_CONFIG.replace('<clear />', '');
const KIT_CONFIG = `<configuration><packageSources><clear /><add key="kit" value="./nuget" /></packageSources></configuration>`;
const FEED_LOCK = {
  name: 'acme', lockfileVersion: 3, packages: {
    '': { name: 'acme', workspaces: ['frontend/acme-web'] },
    'node_modules/@codelab/ng-dga': { version: '1.0.0', resolved: 'file:feed/npm/codelab-ng-dga-1.0.0.tgz' },
    'node_modules/left-pad': { version: '1.3.0', resolved: 'https://registry.npmjs.org/left-pad/-/left-pad-1.3.0.tgz' },
  },
};
const BUILD_PROPS_FEED = '<Project><PropertyGroup><UseCodelabPackageSource Condition="\'$(UseCodelabPackageSource)\' == \'\'">false</UseCodelabPackageSource></PropertyGroup></Project>';
const MANIFEST = { schemaVersion: 1, starterBase: '5a7c9e1b3d5f7a9c1e3b5d7f9a1c3e5b7d9f1a3c' };

const scratch = mkdtempSync(path.join(os.tmpdir(), 'codelab-fork-checks-'));
after(() => rmSync(scratch, { recursive: true, force: true }));
let forkCount = 0;

/** Writes a fork fixture folder: a clean init-ed fork, with `files` overriding or adding files (undefined deletes). */
function fork(files = {}) {
  forkCount += 1;
  const root = path.join(scratch, `fork-${forkCount}`);
  const all = {
    'nuget.config': FEED_CONFIG,
    'Directory.Build.props': BUILD_PROPS_FEED,
    '.npmrc': 'registry=https://registry.npmjs.org/\nsave-exact=true\n',
    'package.json': JSON.stringify({ name: 'acme', workspaces: ['frontend/acme-web'] }),
    'package-lock.json': JSON.stringify(FEED_LOCK),
    'codelab.starter.json': JSON.stringify(MANIFEST),
    'feed/nuget/.gitkeep': '',
    ...files,
  };
  for (const [relative, content] of Object.entries(all)) {
    if (content === undefined) continue;
    const file = path.join(root, relative);
    mkdirSync(path.dirname(file), { recursive: true });
    writeFileSync(file, typeof content === 'string' ? content : JSON.stringify(content));
  }
  return root;
}

function kinds(root, options) {
  return checkFork(root, options).map((finding) => finding.kind);
}

describe('checkFork on fixture forks', () => {
  it('passes a clean init-ed fork', () => {
    assert.deepEqual(checkFork(fork()), []);
  });

  it('fails when packages/ is still in the fork', () => {
    assert.deepEqual(kinds(fork({ 'packages/versions.json': '{}' })), [FindingKind.PackagesFolder]);
  });

  it('fails when the fork still defaults to source mode', () => {
    assert.deepEqual(kinds(fork({ 'Directory.Build.props': BUILD_PROPS_FEED.replace('>false<', '>true<') })), [FindingKind.SourceMode]);
  });

  it('fails when the manifest is missing, invalid or has no starterBase', () => {
    assert.deepEqual(kinds(fork({ 'codelab.starter.json': undefined })), [FindingKind.Manifest]);
    assert.deepEqual(kinds(fork({ 'codelab.starter.json': '{' })), [FindingKind.Manifest]);
    assert.deepEqual(kinds(fork({ 'codelab.starter.json': { ...MANIFEST, starterBase: null } })), [FindingKind.Manifest]);
    assert.deepEqual(kinds(fork({ 'codelab.starter.json': { ...MANIFEST, starterBase: ' ' } })), [FindingKind.Manifest]);
  });

  it('fails when nuget.config has no package source mapping, so nuget.org could serve Codelab.*', () => {
    const findings = checkFork(fork({ 'nuget.config': UNMAPPED_CONFIG }));
    assert.deepEqual(findings.map((finding) => finding.kind), [FindingKind.NugetSource]);
    assert.match(findings[0].message, /nuget\.org/);
  });

  it('fails when Codelab.* is mapped to a remote feed', () => {
    const remote = FEED_CONFIG.replace('value="feed/nuget"', 'value="https://pkgs.example.invalid/nuget/v3/index.json"');
    assert.deepEqual(kinds(fork({ 'nuget.config': remote })), [FindingKind.NugetSource]);
  });

  it('fails when Codelab.* is mapped to the package sources outside feed/', () => {
    const source = FEED_CONFIG.replace('value="feed/nuget"', 'value="../starter/packages/dotnet"');
    assert.deepEqual(kinds(fork({ 'nuget.config': source })), [FindingKind.NugetSource]);
  });

  it('fails when a nuget.config has no <clear/>', () => {
    assert.deepEqual(kinds(fork({ 'nuget.config': NO_CLEAR_CONFIG })), [FindingKind.NugetNoClear]);
  });

  it('checks nested nuget.config files relative to their own folder, and skips build and package folders', () => {
    const nested = FEED_CONFIG.replace('value="feed/nuget"', 'value="../../feed/nuget"');
    assert.deepEqual(checkFork(fork({ 'samples/app/nuget.config': nested })), []);
    assert.deepEqual(kinds(fork({ 'samples/app/nuget.config': FEED_CONFIG })), [FindingKind.NugetSource]);
    assert.deepEqual(checkFork(fork({ 'node_modules/x/nuget.config': UNMAPPED_CONFIG, 'src/bin/nuget.config': UNMAPPED_CONFIG })), []);
  });

  it('accepts a handover kit nuget folder with --kit and an allowed hosted feed with --allow-source', () => {
    const kit = path.join(scratch, 'kit');
    mkdirSync(path.join(kit, 'nuget'), { recursive: true });
    const kitRoot = fork({ 'nuget.config': KIT_CONFIG.replace('./nuget', kit.replaceAll('\\', '/') + '/nuget') });
    assert.deepEqual(kinds(kitRoot), [FindingKind.NugetSource]);
    assert.deepEqual(checkFork(kitRoot, { kitDirs: [kit] }), []);

    const hosted = 'https://nuget.pkg.github.example.invalid/acme/index.json';
    const hostedRoot = fork({ 'nuget.config': FEED_CONFIG.replace('value="feed/nuget"', `value="${hosted}"`) });
    assert.deepEqual(kinds(hostedRoot), [FindingKind.NugetSource]);
    assert.deepEqual(checkFork(hostedRoot, { allowedSources: ['https://nuget.pkg.github.example.invalid/acme/'] }), []);
  });

  it('fails when .npmrc points @codelab at a registry that is not allowed', () => {
    const npmrc = 'registry=https://registry.npmjs.org/\n@codelab:registry=https://npm.example.invalid/\n';
    assert.deepEqual(kinds(fork({ '.npmrc': npmrc })), [FindingKind.NpmRegistry]);
    assert.deepEqual(checkFork(fork({ '.npmrc': npmrc }), { allowedSources: ['https://npm.example.invalid/'] }), []);
  });

  it('fails when package-lock links @codelab/* from workspace source', () => {
    const lock = structuredClone(FEED_LOCK);
    lock.packages['node_modules/@codelab/ng-dga'] = { resolved: 'packages/npm/ng-dga', link: true };
    assert.deepEqual(kinds(fork({ 'package-lock.json': lock })), [FindingKind.NpmLock]);
  });

  it('fails when package-lock resolves @codelab/* from a registry, or has no resolved source', () => {
    const remote = structuredClone(FEED_LOCK);
    remote.packages['frontend/acme-web/node_modules/@codelab/ng-security'] = { version: '1.0.0', resolved: 'https://registry.npmjs.org/@codelab/ng-security/-/ng-security-1.0.0.tgz' };
    assert.deepEqual(kinds(fork({ 'package-lock.json': remote })), [FindingKind.NpmLock]);
    const unresolved = structuredClone(FEED_LOCK);
    unresolved.packages['node_modules/@codelab/ng-dga'] = { version: '1.0.0' };
    assert.deepEqual(kinds(fork({ 'package-lock.json': unresolved })), [FindingKind.NpmLock]);
  });

  it('fails when the lockfile is invalid, or missing next to a package.json', () => {
    assert.deepEqual(kinds(fork({ 'package-lock.json': '{' })), [FindingKind.NpmLock]);
    assert.deepEqual(kinds(fork({ 'package-lock.json': undefined })), [FindingKind.NpmLockMissing]);
  });

  it('reports every problem of a starter-like (not init-ed) tree at once', () => {
    const lock = structuredClone(FEED_LOCK);
    lock.packages['node_modules/@codelab/ng-dga'] = { resolved: 'packages/npm/ng-dga', link: true };
    const root = fork({
      'packages/versions.json': '{}',
      'Directory.Build.props': BUILD_PROPS_FEED.replace('>false<', '>true<'),
      'package-lock.json': lock,
      'codelab.starter.json': { ...MANIFEST, starterBase: null },
    });
    assert.deepEqual(kinds(root).sort(), [FindingKind.Manifest, FindingKind.NpmLock, FindingKind.PackagesFolder, FindingKind.SourceMode].sort());
  });
});

describe('NuGet source mapping', () => {
  it('parses sources, <clear/> and the mapping, ignoring comments', () => {
    const config = parseNugetConfig(FEED_CONFIG);
    assert.equal(config.cleared, true);
    assert.deepEqual(config.sources.map((source) => source.key), ['codelab-local', 'nuget.org']);
    assert.deepEqual(config.mapping, { 'codelab-local': ['Codelab.*'], 'nuget.org': ['*'] });
    assert.equal(parseNugetConfig(UNMAPPED_CONFIG).mapping, undefined);
  });

  it('ranks an exact id above a longer prefix above a shorter prefix', () => {
    assert.ok(patternSpecificity('Codelab.Probe', 'codelab.probe') > patternSpecificity('Codelab.*', 'Codelab.Probe'));
    assert.ok(patternSpecificity('Codelab.*', 'Codelab.Probe') > patternSpecificity('*', 'Codelab.Probe'));
    assert.ok(patternSpecificity('Other.*', 'Codelab.Probe') < patternSpecificity('*', 'Codelab.Probe'));
  });

  it('serves Codelab.* from the most specific mapped source only, or from every source without a mapping', () => {
    assert.deepEqual(sourcesServing(parseNugetConfig(FEED_CONFIG), 'Codelab.Probe'), ['codelab-local']);
    assert.deepEqual(sourcesServing(parseNugetConfig(UNMAPPED_CONFIG), 'Codelab.Probe'), ['codelab-local', 'nuget.org']);
  });
});

describe('isAllowedSource', () => {
  const root = path.resolve(path.sep, 'forks', 'acme');
  const options = { root, baseDir: root, kitDirs: [], allowedSources: [] };

  it('accepts folders inside feed/ (with or without file:) and refuses feed look-alikes', () => {
    assert.ok(isAllowedSource('feed/nuget', options));
    assert.ok(isAllowedSource('file:feed/npm/codelab-ng-dga-1.0.0.tgz', options));
    assert.ok(!isAllowedSource('feedback/nuget', options));
    assert.ok(!isAllowedSource('../feed/nuget', options));
  });

  it('refuses remote URLs unless their prefix is allowed', () => {
    assert.ok(!isAllowedSource('https://api.nuget.org/v3/index.json', options));
    assert.ok(isAllowedSource('https://feed.example.invalid/x', { ...options, allowedSources: ['https://feed.example.invalid/'] }));
  });
});

describe('the CLI', () => {
  const quiet = { log: () => {}, logError: () => {} };

  it('parses repeated --kit and --allow-source', () => {
    assert.deepEqual(parseArguments(['--root', 'f', '--kit', 'a', '--kit', 'b', '--allow-source', 'https://x/']),
      { root: 'f', kitDirs: ['a', 'b'], allowedSources: ['https://x/'] });
    assert.ok(parseArguments(['--root']).error);
    assert.ok(parseArguments(['--bogus', 'x']).error);
  });

  it('exits 0 for a clean fork, 1 with findings and 2 for usage errors', () => {
    assert.equal(main(['--root', fork()], quiet.log, quiet.logError), EXIT_CLEAN);
    assert.equal(main(['--root', fork({ 'packages/x.txt': 'x' })], quiet.log, quiet.logError), EXIT_FINDINGS);
    assert.equal(main(['--root', path.join(scratch, 'missing')], quiet.log, quiet.logError), EXIT_USAGE);
    assert.equal(main(['--nope'], quiet.log, quiet.logError), EXIT_USAGE);
  });
});
