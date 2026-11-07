// Run: node --test "tools/ci/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  EXIT_OK,
  EXIT_REFUSED,
  EXIT_USAGE,
  PackageKind,
  PlanMode,
  CHANGELOG_FILE,
  VERSIONS_FILE,
  buildPlan,
  feedFileName,
  isInFeed,
  kindOf,
  main,
  nextCiNumber,
  parseArguments,
  readKnownInsecure,
  readUnreleased,
  readVersions,
  sourceOf,
  stampManifest,
  unshippedApi,
} from '../package-versions.mjs';

const REPO_ROOT = fileURLToPath(new URL('../../../', import.meta.url));
const BUILD_SCRIPT = path.join(REPO_ROOT, 'tools', 'build.ps1');
const SDK_ID = 'Codelab.Build.Sdk';
const SAMPLE_VERSIONS = Object.freeze({ 'Codelab.Alpha': '0.5.0', '@codelab/ng-beta': '0.1.0' });
const ALPHA_UNSHIPPED = 'packages/dotnet/Codelab.Alpha/PublicAPI.Unshipped.txt';
const NULLABLE_HEADER = '#nullable enable';
const ALPHA_API_LINE = 'static Codelab.Alpha.Scope.Inbound.get -> object!';
/** Files that pin Codelab package versions: `Include="<id>" Version="x.y.z"` or `<IdWithoutDotsVersion ...>x.y.z<`. */
const PACKAGE_PIN_FILES = Object.freeze(['Directory.Packages.props', 'packages/samples/Codelab.Versions.props']);
const tempRoots = [];

function tempDir(prefix) {
  const dir = mkdtempSync(path.join(tmpdir(), prefix));
  tempRoots.push(dir);
  return dir;
}

function writeFile(root, relative, content = '') {
  const file = path.join(root, relative);
  mkdirSync(path.dirname(file), { recursive: true });
  writeFileSync(file, content);
  return file;
}

/** A fake repo with versions.json, both package sources and an empty feed. */
function fakeRepo(versions = SAMPLE_VERSIONS) {
  const root = tempDir('pkg-versions-');
  writeFile(root, VERSIONS_FILE, JSON.stringify(versions));
  writeFile(root, 'packages/dotnet/Codelab.Alpha/Codelab.Alpha.csproj', '<Project />');
  writeFile(root, 'packages/npm/ng-beta/package.json', JSON.stringify({ name: '@codelab/ng-beta', version: '0.0.0' }));
  mkdirSync(path.join(root, 'feed', 'nuget'), { recursive: true });
  mkdirSync(path.join(root, 'feed', 'npm'), { recursive: true });
  return { root, feed: path.join(root, 'feed') };
}

/** A changelog whose package sections carry an [Unreleased] entry only for the ids in `pending` (id -> version). */
function writeChangelog(root, pending) {
  const sections = Object.keys(SAMPLE_VERSIONS).map((id) => {
    const unreleased = id in pending ? [`### [Unreleased] - ${pending[id]}`, '', '- Fixed.', ''] : [];
    return [`## ${id}`, '', ...unreleased, `### [${SAMPLE_VERSIONS[id]}] - 2026-11-04`, '', '- Added.', ''];
  });
  writeFile(root, CHANGELOG_FILE, ['# Changelog', '', ...sections.flat()].join('\n'));
}

afterEach(() => {
  while (tempRoots.length > 0) rmSync(tempRoots.pop(), { recursive: true, force: true });
});

describe('package naming', () => {
  it('derives kind, source and feed file name from the id', () => {
    assert.equal(kindOf('Codelab.Security'), PackageKind.NuGet);
    assert.equal(kindOf('@codelab/ng-tools'), PackageKind.Npm);
    assert.equal(sourceOf('Codelab.Security'), 'packages/dotnet/Codelab.Security/Codelab.Security.csproj');
    assert.equal(sourceOf('@codelab/ng-tools'), 'packages/npm/ng-tools');
    assert.equal(feedFileName('Codelab.Security', '0.5.0-ci.3'), 'Codelab.Security.0.5.0-ci.3.nupkg');
    assert.equal(feedFileName('@codelab/ng-tools', '0.1.0'), 'codelab-ng-tools-0.1.0.tgz');
  });
});

describe('readVersions', () => {
  it('accepts plain x.y.z versions for Codelab ids', () => {
    const { root } = fakeRepo();
    assert.deepEqual(readVersions(root), { versions: { ...SAMPLE_VERSIONS }, errors: [] });
  });

  it('rejects prerelease versions, foreign ids, non-objects and a missing file', () => {
    const { root } = fakeRepo({ 'Codelab.Alpha': '0.5.0-ci.1', 'Other.Package': '1.0.0', '@other/x': '1.0.0' });
    assert.equal(readVersions(root).errors.length, 3);
    writeFile(root, VERSIONS_FILE, '[]');
    assert.equal(readVersions(root).errors.length, 1);
    writeFile(root, VERSIONS_FILE, '{ nope');
    assert.match(readVersions(root).errors[0], /not valid JSON/);
    assert.match(readVersions(tempDir('empty-')).errors[0], /not found/);
  });
});

describe('nextCiNumber', () => {
  it('is 1 for an empty or missing feed', () => {
    assert.equal(nextCiNumber(fakeRepo().feed), 1);
    assert.equal(nextCiNumber(path.join(tempDir('nofeed-'), 'feed')), 1);
  });

  it('is one above the highest ci number across both feed folders', () => {
    const { feed } = fakeRepo();
    writeFile(feed, 'nuget/Codelab.Alpha.0.5.0-ci.4.nupkg');
    writeFile(feed, 'npm/codelab-ng-beta-0.1.0-ci.9.tgz');
    writeFile(feed, 'npm/codelab-ng-beta-0.1.0.tgz');
    writeFile(feed, 'nuget/.gitkeep');
    assert.equal(nextCiNumber(feed), 10);
  });
});

describe('buildPlan', () => {
  it('gives every package the same ci number in ci mode', () => {
    const { root, feed } = fakeRepo();
    const plan = buildPlan({ root, feed, mode: PlanMode.Ci, ciNumber: 7 });
    assert.deepEqual(plan.errors, []);
    assert.deepEqual(plan.packages.map((p) => p.version), ['0.5.0-ci.7', '0.1.0-ci.7']);
    assert.equal(plan.packages[0].feedFile, path.join(feed, 'nuget', 'Codelab.Alpha.0.5.0-ci.7.nupkg'));
    assert.equal(plan.packages[1].feedFile, path.join(feed, 'npm', 'codelab-ng-beta-0.1.0-ci.7.tgz'));
  });

  it('uses the exact versions in release mode', () => {
    const { root, feed } = fakeRepo();
    const plan = buildPlan({ root, feed, mode: PlanMode.Release });
    assert.deepEqual(plan.errors, []);
    assert.equal(plan.ciNumber, null);
    assert.deepEqual(plan.packages.map((p) => p.version), ['0.5.0', '0.1.0']);
  });

  it('refuses a release whose version is in the feed but still has an [Unreleased] entry (NuGet any case, npm)', () => {
    const { root, feed } = fakeRepo();
    writeChangelog(root, { 'Codelab.Alpha': '0.5.0', '@codelab/ng-beta': '0.1.0' });
    writeFile(feed, 'nuget/codelab.alpha/0.5.0/codelab.alpha.0.5.0.nupkg');
    writeFile(feed, 'npm/codelab-ng-beta-0.1.0.tgz');
    const plan = buildPlan({ root, feed, mode: PlanMode.Release });
    assert.equal(plan.errors.length, 2);
    assert.ok(plan.errors.every((error) => error.startsWith('REFUSED:')));
    assert.ok(plan.packages.every((p) => p.inFeed && !p.skip));
  });

  // Regression: one unchanged, already released package blocked every other release.
  it('skips a released, unchanged package and releases the changed one', () => {
    const { root, feed } = fakeRepo();
    writeChangelog(root, { 'Codelab.Alpha': '0.5.0' });
    writeFile(feed, 'npm/codelab-ng-beta-0.1.0.tgz');
    const plan = buildPlan({ root, feed, mode: PlanMode.Release });
    assert.deepEqual(plan.errors, []);
    assert.deepEqual(plan.packages.map((p) => [p.id, p.skip]), [['Codelab.Alpha', false], ['@codelab/ng-beta', true]]);
  });

  it('refuses a release with nothing to pack and an [Unreleased] version that differs from versions.json', () => {
    const { root, feed } = fakeRepo();
    writeChangelog(root, {});
    writeFile(feed, 'nuget/Codelab.Alpha.0.5.0.nupkg');
    writeFile(feed, 'npm/codelab-ng-beta-0.1.0.tgz');
    assert.ok(buildPlan({ root, feed, mode: PlanMode.Release }).errors.some((error) => error.includes('nothing to release')));
    const other = fakeRepo();
    writeChangelog(other.root, { 'Codelab.Alpha': '0.6.0' });
    assert.ok(buildPlan({ root: other.root, feed: other.feed, mode: PlanMode.Release }).errors.some((error) => error.includes('0.6.0')));
  });

  it('reads the [Unreleased] version of each package section', () => {
    const { root } = fakeRepo();
    writeChangelog(root, { '@codelab/ng-beta': '0.1.1' });
    assert.deepEqual([...readUnreleased(root)], [['@codelab/ng-beta', '0.1.1']]);
    assert.equal(readUnreleased(tempDir('no-changelog-')).size, 0);
  });

  it('does not treat a prerelease of the same version as the release', () => {
    const { root, feed } = fakeRepo();
    writeFile(feed, 'nuget/Codelab.Alpha.0.5.0-ci.1.nupkg');
    assert.equal(isInFeed(feed, 'Codelab.Alpha', '0.5.0'), false);
    assert.deepEqual(buildPlan({ root, feed, mode: PlanMode.Release }).errors, []);
  });

  // Regression: Codelab.Observability changed after 0.5.1 shipped (a security fix plus a new public API), but
  // versions.json still said 0.5.1, which was in the feed, so the release skipped it and every fork kept 0.5.1.
  it('refuses a released NuGet package whose PublicAPI.Unshipped.txt lists API lines', () => {
    const { root, feed } = fakeRepo();
    writeChangelog(root, {});
    writeFile(feed, 'nuget/Codelab.Alpha.0.5.0.nupkg');
    writeFile(root, ALPHA_UNSHIPPED, [NULLABLE_HEADER, ALPHA_API_LINE, ''].join('\r\n'));
    const plan = buildPlan({ root, feed, mode: PlanMode.Release });
    const refusal = plan.errors.find((error) => error.includes('Codelab.Alpha 0.5.0'));
    assert.ok(refusal?.startsWith('REFUSED:'), plan.errors.join('; '));
    assert.ok(refusal.includes(ALPHA_API_LINE));
    assert.equal(plan.packages.find((p) => p.id === 'Codelab.Alpha').skip, false);
  });

  it('still skips a released package whose PublicAPI.Unshipped.txt holds only comments and blank lines', () => {
    const { root, feed } = fakeRepo();
    writeChangelog(root, { '@codelab/ng-beta': '0.1.0' });
    writeFile(feed, 'nuget/Codelab.Alpha.0.5.0.nupkg');
    writeFile(root, ALPHA_UNSHIPPED, [NULLABLE_HEADER, '', ''].join('\n'));
    const plan = buildPlan({ root, feed, mode: PlanMode.Release });
    assert.deepEqual(plan.errors, []);
    assert.equal(plan.packages.find((p) => p.id === 'Codelab.Alpha').skip, true);
  });

  it('releases a bumped package whose unshipped API is not in the feed yet', () => {
    const { root, feed } = fakeRepo({ ...SAMPLE_VERSIONS, 'Codelab.Alpha': '0.5.1' });
    writeChangelog(root, { 'Codelab.Alpha': '0.5.1' });
    writeFile(feed, 'nuget/Codelab.Alpha.0.5.0.nupkg');
    writeFile(root, ALPHA_UNSHIPPED, [NULLABLE_HEADER, ALPHA_API_LINE].join('\n'));
    assert.deepEqual(buildPlan({ root, feed, mode: PlanMode.Release }).errors, []);
  });

  it('reads unshipped API lines only for NuGet packages that have the file', () => {
    const { root } = fakeRepo();
    assert.deepEqual(unshippedApi(root, 'Codelab.Alpha'), []);
    writeFile(root, ALPHA_UNSHIPPED, [NULLABLE_HEADER, '', `  ${ALPHA_API_LINE}  `].join('\n'));
    assert.deepEqual(unshippedApi(root, 'Codelab.Alpha'), [ALPHA_API_LINE]);
    assert.deepEqual(unshippedApi(root, '@codelab/ng-beta'), []);
  });

  it('refuses a versions.json version the changelog marks Known insecure, in ci and release mode', () => {
    const { root, feed } = fakeRepo();
    const heading = '### [0.5.0] - 2026-11-04 - Known insecure: do not use; upgrade to 0.5.1';
    writeFile(root, CHANGELOG_FILE, ['# Changelog', '', '## Codelab.Alpha', '', heading, ''].join('\n'));
    assert.deepEqual([...readKnownInsecure(root)].map(([id, set]) => [id, [...set]]), [['Codelab.Alpha', ['0.5.0']]]);
    for (const mode of [PlanMode.Ci, PlanMode.Release]) {
      const { errors } = buildPlan({ root, feed, mode, ciNumber: 1 });
      assert.ok(errors.some((error) => error.includes('Known insecure') && error.includes('Codelab.Alpha 0.5.0')), mode);
    }
    assert.equal(readKnownInsecure(tempDir('no-changelog-')).size, 0);
  });

  it('refuses a ci number that is already in the feed, a missing source and a bad ci number', () => {
    const { root, feed } = fakeRepo();
    writeFile(feed, 'nuget/Codelab.Alpha.0.5.0-ci.2.nupkg');
    rmSync(path.join(root, 'packages', 'npm'), { recursive: true });
    const plan = buildPlan({ root, feed, mode: PlanMode.Ci, ciNumber: 2 });
    assert.equal(plan.errors.length, 2);
    assert.ok(plan.errors.some((error) => error.startsWith('MISSING: packages/npm/ng-beta')));
    assert.equal(buildPlan({ root, feed, mode: PlanMode.Ci, ciNumber: 0 }).errors.length, 2);
  });
});

describe('stampManifest', () => {
  it('rewrites only the version of a staged package.json', () => {
    const dir = tempDir('stamp-');
    const manifest = writeFile(dir, 'package.json', JSON.stringify({ name: '@codelab/x', version: '0.0.0', private: false }));
    stampManifest(manifest, '0.5.0-ci.3');
    const text = readFileSync(manifest, 'utf8');
    assert.deepEqual(JSON.parse(text), { name: '@codelab/x', version: '0.5.0-ci.3', private: false });
    assert.ok(text.endsWith('}\n'));
  });
});

describe('command line', () => {
  it('parses commands, valued options and the --release flag', () => {
    assert.deepEqual(parseArguments(['plan', '--root', '.', '--release']), { command: 'plan', options: { root: '.', release: true } });
    assert.deepEqual(parseArguments(['plan', '--ci', '4']), { command: 'plan', options: { ci: '4' } });
    assert.equal(parseArguments([]), null);
    assert.equal(parseArguments(['--root', '.']), null);
    assert.equal(parseArguments(['plan', '--root']), null);
    assert.equal(parseArguments(['plan', 'stray']), null);
  });

  it('exits 0 for a clean plan, 1 for a refusal and 2 for a usage error', (t) => {
    t.mock.method(console, 'log', () => {});
    t.mock.method(console, 'error', () => {});
    const { root, feed } = fakeRepo();
    assert.equal(main(['plan', '--root', root, '--ci', '1']), EXIT_OK);
    writeChangelog(root, { '@codelab/ng-beta': '0.1.0' });
    writeFile(feed, 'npm/codelab-ng-beta-0.1.0.tgz');
    assert.equal(main(['plan', '--root', root, '--release']), EXIT_REFUSED);
    assert.equal(main(['plan', '--root', root, '--release', '--feed', path.join(root, 'other-feed')]), EXIT_OK);
    assert.equal(main(['unknown']), EXIT_USAGE);
    assert.equal(main(['stamp', '--manifest', 'x']), EXIT_USAGE);
  });
});

describe('repository contract', () => {
  const { versions, errors } = readVersions(REPO_ROOT);

  it('packages/versions.json is valid and every listed source exists', () => {
    assert.deepEqual(errors, []);
    const plan = buildPlan({ root: REPO_ROOT, feed: tempDir('feed-'), mode: PlanMode.Release });
    assert.deepEqual(plan.errors, []);
  });

  it('lists every Codelab package project and every npm workspace package', () => {
    const dotnetRoot = path.join(REPO_ROOT, 'packages', 'dotnet');
    const packageProjects = readdirSync(dotnetRoot)
      .map((name) => path.join(dotnetRoot, name, `${name}.csproj`))
      .filter((file) => {
        try {
          return /<CodelabPackage>true<\/CodelabPackage>/.test(readFileSync(file, 'utf8'));
        } catch {
          return false;
        }
      })
      .map((file) => path.basename(file, '.csproj'));
    const npmRoot = path.join(REPO_ROOT, 'packages', 'npm');
    const npmNames = readdirSync(npmRoot).map(
      (name) => JSON.parse(readFileSync(path.join(npmRoot, name, 'package.json'), 'utf8')).name,
    );
    for (const id of [SDK_ID, ...packageProjects, ...npmNames]) assert.ok(id in versions, `${id} is not in ${VERSIONS_FILE}`);
  });

  it('keeps the Codelab.Build.Sdk csproj fallback version equal to versions.json', () => {
    const project = readFileSync(path.join(REPO_ROOT, sourceOf(SDK_ID)), 'utf8');
    const pattern = /<CodelabBuildSdkBaseVersion>(\d[^<]*)<\/CodelabBuildSdkBaseVersion>/;
    assert.equal(pattern.exec(project)?.[1], versions[SDK_ID]);
  });

  /** Every `<id>` -> version pinned by a file: PackageVersion items and `<IdWithoutDotsVersion>` properties. */
  function pinsOf(relative) {
    const text = readFileSync(path.join(REPO_ROOT, relative), 'utf8');
    const pins = [];
    for (const match of text.matchAll(/<PackageVersion Include="(Codelab\.[^"]+)" Version="(\d+\.\d+\.\d+)"/g)) {
      pins.push([match[1], match[2]]);
    }
    for (const id of Object.keys(versions).filter((name) => kindOf(name) === PackageKind.NuGet)) {
      const property = `${id.replaceAll('.', '')}Version`;
      const match = new RegExp(`<${property}[^>]*>(\\d+\\.\\d+\\.\\d+)</${property}>`).exec(text);
      if (match) pins.push([id, match[1]]);
    }
    return pins;
  }

  // Regression: Directory.Packages.props kept Codelab.Observability 0.5.1 after a caller's traceparent was found to
  // set its correlation id; every fork in feed mode restored that version.
  it('no package pin names a version the changelog marks Known insecure', () => {
    const insecure = readKnownInsecure(REPO_ROOT);
    const offending = PACKAGE_PIN_FILES.flatMap((file) =>
      pinsOf(file).filter(([id, version]) => insecure.get(id)?.has(version)).map(([id, version]) => `${file}: ${id} ${version}`),
    );
    assert.deepEqual(offending, []);
  });

  it('the sample pins every NuGet package at its versions.json version', () => {
    const samplePins = new Map(pinsOf('packages/samples/Codelab.Versions.props'));
    for (const id of Object.keys(versions).filter((name) => kindOf(name) === PackageKind.NuGet)) {
      assert.equal(samplePins.get(id), versions[id], `${id} in packages/samples/Codelab.Versions.props`);
    }
  });

  // Regression: a package whose source changed after its release was skipped by the release because its
  // versions.json version was already in the local feed. Only meaningful where the feed holds releases.
  it('no package in the local feed has public API changes that are not in a newer versions.json version', () => {
    const feed = path.join(REPO_ROOT, 'feed');
    const plan = buildPlan({ root: REPO_ROOT, feed, mode: PlanMode.Release });
    const drift = plan.errors.filter((error) => error.includes('PublicAPI.Unshipped.txt'));
    assert.deepEqual(drift, []);
  });
});

describe('build.ps1 contract', () => {
  const script = readFileSync(BUILD_SCRIPT, 'utf8');

  it('runs CI steps 11 (npm build+lint+test) and 12 (vectors verify for two prefixes) after step 10', () => {
    const order = ['10 scanner tests', '11 npm build+lint+test', '12 vectors verify'].map((name) => script.indexOf(`'${name}'`));
    assert.ok(order.every((index) => index > 0), 'every step is declared');
    assert.deepEqual([...order].sort((a, b) => a - b), order);
    assert.match(script, /tools\/vectors\/verify-vectors\.mjs/);
    assert.ok(script.includes("'tools/ci/lint-packages.mjs'"), 'step 11 runs the package lint');
    assert.match(script, /'CODELABTEST'/);
    assert.match(script, /'ALTPFX'/);
  });

  it('plans -Pack and -Release through package-versions.mjs and has a -Release switch', () => {
    assert.match(script, /\[switch\] \$Release/);
    assert.match(script, /tools\/ci\/package-versions\.mjs/);
    assert.match(script, /'--release'/);
  });

  /** The body of a PowerShell function, up to the next top-level function. */
  function functionBody(name) {
    const start = script.indexOf(`function ${name}`);
    assert.ok(start >= 0, `${name} exists`);
    const next = script.indexOf('\nfunction ', start + 1);
    return script.slice(start, next < 0 ? undefined : next);
  }

  // Regression: CI step 13 rewrote Directory.Build.props and pushed eight packages to feed\ on every run.
  it('CI step 13 packs into a temporary feed and never publishes to feed\\ or rewrites the SDK pin', () => {
    const step13 = functionBody('Invoke-PackageIntegrationStep');
    assert.ok(!step13.includes('Invoke-PackTarget'), 'step 13 does not run the -Pack target');
    assert.ok(step13.includes('New-TemporaryPrerelease'));
    const temporary = functionBody('New-TemporaryPrerelease');
    for (const forbidden of ['Publish-ToFeed', 'Set-PinnedBuildSdkVersion', 'Invoke-FeedModeBuild', '$NuGetFeedDir', '$NpmFeedDir']) {
      assert.ok(!temporary.includes(forbidden), `New-TemporaryPrerelease must not use ${forbidden}`);
    }
    assert.ok(functionBody('Invoke-CiTarget').includes('Remove-TemporaryFeeds'), 'CI removes its temporary feeds');
  });

  // Regression: under Set-StrictMode a missing hashtable key failed steps 13 and 14 for pinned releases.
  it('pinned and prerelease sample versions both carry the Feed and Root keys steps 13 and 14 read', () => {
    for (const name of ['Get-SamplePins', 'Get-PrereleaseOverrides']) {
      const body = functionBody(name);
      assert.match(body, /Feed = \$null; Root = \$null/, `${name} sets Feed and Root`);
    }
  });
});
