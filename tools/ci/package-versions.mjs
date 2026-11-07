#!/usr/bin/env node
// Package version plan for tools/build.ps1 -Pack: packages/versions.json is the one source of every Codelab package
// version (explicit-version-file semver, see tools/README.md "Versioning").
//
// Usage:
//   node tools/ci/package-versions.mjs plan --root <path> --ci <n> [--feed <dir>]
//       prints the pack plan as JSON; every package gets <version>-ci.<n> (the same <n> for all of them).
//   node tools/ci/package-versions.mjs plan --root <path> --release [--feed <dir>]
//       prints the plan for the exact versions.json versions. A package whose exact version is already in the feed
//       and whose packages/CHANGELOG.md section has no [Unreleased] entry is released and unchanged: it is planned
//       with skip=true (not packed). One that is in the feed but has an [Unreleased] entry is refused (bump it), and
//       so is a NuGet package in the feed whose PublicAPI.Unshipped.txt lists API lines: its source changed after the
//       release (a release moves those lines to PublicAPI.Shipped.txt), so the feed version is not the source.
//   In both modes a versions.json version that packages/CHANGELOG.md marks "Known insecure" is refused.
//   node tools/ci/package-versions.mjs next-ci --root <path> [--feed <dir>]
//       prints one above the highest ci.<n> found in the feed (1 for an empty feed).
//   node tools/ci/package-versions.mjs stamp --manifest <package.json> --version <semver>
//       writes the version into a (staged) npm package.json.
// Exit codes: 0 ok, 1 refused (a planned version is already in the feed, a package source is missing or
// versions.json is invalid), 2 usage error.
//
// A package id starting with '@' is an npm package at packages/npm/<name>, packed to <feed>/npm; any other id is a
// NuGet package at packages/dotnet/<Id>/<Id>.csproj, packed to <feed>/nuget.

import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const EXIT_OK = 0;
export const EXIT_REFUSED = 1;
export const EXIT_USAGE = 2;

export const VERSIONS_FILE = 'packages/versions.json';
export const CHANGELOG_FILE = 'packages/CHANGELOG.md';
export const DEFAULT_FEED = 'feed';
export const CI_LABEL = 'ci';
export const PackageKind = Object.freeze({ NuGet: 'nuget', Npm: 'npm' });
export const PlanMode = Object.freeze({ Ci: 'ci', Release: 'release' });

const NPM_SCOPE_PREFIX = '@';
const NUGET_EXTENSION = '.nupkg';
const NPM_EXTENSION = '.tgz';
const RELEASE_VERSION_PATTERN = /^\d+\.\d+\.\d+$/;
const NUGET_ID_PATTERN = /^Codelab(\.[A-Z][A-Za-z0-9]*)+$/;
const NPM_ID_PATTERN = /^@codelab\/[a-z][a-z0-9-]*$/;
const CI_FILE_PATTERN = new RegExp(`-${CI_LABEL}\\.(\\d+)\\.(nupkg|tgz)$`, 'i');
const JSON_INDENT = 2;
const PACKAGE_HEADING = /^## (\S+)\s*$/;
const UNRELEASED_HEADING = /^### \[Unreleased\](?:\s*-\s*(\S+))?/;
const KNOWN_INSECURE_HEADING = /^### \[(\d+\.\d+\.\d+)\].*\bKnown insecure\b/i;
const UNSHIPPED_API_FILE = 'PublicAPI.Unshipped.txt';
const API_FILE_COMMENT = '#';
const FIRST_CI_NUMBER = 1;
const NO_CI_NUMBER = 0;

/**
 * Reads and validates versions.json. Release versions are plain x.y.z: the prerelease label is added by the pack.
 * @returns {{ versions: Record<string, string>, errors: string[] }}
 */
export function readVersions(root) {
  const file = path.join(root, VERSIONS_FILE);
  if (!existsSync(file)) return { versions: {}, errors: [`${VERSIONS_FILE} not found.`] };
  let versions;
  try {
    versions = JSON.parse(readFileSync(file, 'utf8'));
  } catch (error) {
    return { versions: {}, errors: [`${VERSIONS_FILE} is not valid JSON: ${error.message}`] };
  }
  if (versions === null || typeof versions !== 'object' || Array.isArray(versions)) {
    return { versions: {}, errors: [`${VERSIONS_FILE} must be an object of "<package id>": "<x.y.z>".`] };
  }
  return { versions, errors: Object.entries(versions).flatMap(([id, version]) => validateEntry(id, version)) };
}

function validateEntry(id, version) {
  const errors = [];
  if (!NUGET_ID_PATTERN.test(id) && !NPM_ID_PATTERN.test(id)) {
    errors.push(`'${id}' is neither a Codelab.* NuGet id nor an @codelab/* npm name.`);
  }
  if (typeof version !== 'string' || !RELEASE_VERSION_PATTERN.test(version)) {
    errors.push(`'${id}' version '${version}' must be a plain x.y.z release version.`);
  }
  return errors;
}

export function kindOf(id) {
  return id.startsWith(NPM_SCOPE_PREFIX) ? PackageKind.Npm : PackageKind.NuGet;
}

/** Repo-relative source of a package: the csproj for NuGet, the package folder for npm. */
export function sourceOf(id) {
  if (kindOf(id) === PackageKind.Npm) return `packages/npm/${id.split('/')[1]}`;
  return `packages/dotnet/${id}/${id}.csproj`;
}

/** File name the pack produces: <Id>.<version>.nupkg, or npm's <scope>-<name>-<version>.tgz. */
export function feedFileName(id, version) {
  if (kindOf(id) === PackageKind.NuGet) return `${id}.${version}${NUGET_EXTENSION}`;
  return `${id.slice(NPM_SCOPE_PREFIX.length).replace('/', '-')}-${version}${NPM_EXTENSION}`;
}

export function feedFolderOf(feed, id) {
  return path.join(feed, kindOf(id));
}

function listFiles(directory) {
  if (!existsSync(directory)) return [];
  return readdirSync(directory, { withFileTypes: true, recursive: true })
    .filter((entry) => entry.isFile())
    .map((entry) => entry.name);
}

/** True when the feed folder already holds that file (any depth, any case: NuGet folder feeds may lower-case). */
export function isInFeed(feed, id, version) {
  const wanted = feedFileName(id, version).toLowerCase();
  return listFiles(feedFolderOf(feed, id)).some((name) => name.toLowerCase() === wanted);
}

/** One above the highest ci.<n> in either feed folder, so every package of the next run shares one fresh <n>. */
export function nextCiNumber(feed) {
  const numbers = Object.values(PackageKind)
    .flatMap((kind) => listFiles(path.join(feed, kind)))
    .map((name) => CI_FILE_PATTERN.exec(name))
    .filter((match) => match !== null)
    .map((match) => Number(match[1]));
  return Math.max(NO_CI_NUMBER, ...numbers) + FIRST_CI_NUMBER;
}

/**
 * The version under each package's `### [Unreleased] - <version>` heading in packages/CHANGELOG.md (null when the
 * package section has no Unreleased entry, or the version is not given). A missing changelog gives an empty map.
 * @returns {Map<string, string | null>}
 */
export function readUnreleased(root) {
  const file = path.join(root, CHANGELOG_FILE);
  const unreleased = new Map();
  if (!existsSync(file)) return unreleased;
  let current = null;
  for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
    const heading = PACKAGE_HEADING.exec(line);
    if (heading) {
      current = heading[1];
      continue;
    }
    const entry = current === null ? null : UNRELEASED_HEADING.exec(line);
    if (entry && !unreleased.has(current)) unreleased.set(current, entry[1] ?? null);
  }
  return unreleased;
}

/**
 * The versions packages/CHANGELOG.md marks **Known insecure** (`### [x.y.z] - <date> - Known insecure: ...`), per
 * package section. A pin to one of them must be upgraded. A missing changelog gives an empty map.
 * @returns {Map<string, Set<string>>}
 */
export function readKnownInsecure(root) {
  const file = path.join(root, CHANGELOG_FILE);
  const insecure = new Map();
  if (!existsSync(file)) return insecure;
  let current = null;
  for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
    const heading = PACKAGE_HEADING.exec(line);
    if (heading) {
      current = heading[1];
      continue;
    }
    const entry = current === null ? null : KNOWN_INSECURE_HEADING.exec(line);
    if (!entry) continue;
    if (!insecure.has(current)) insecure.set(current, new Set());
    insecure.get(current).add(entry[1]);
  }
  return insecure;
}

/**
 * The public API lines of a NuGet package's PublicAPI.Unshipped.txt (comments such as `#nullable enable` and blank
 * lines dropped); empty for an npm package or a project without the file. A release moves these lines into
 * PublicAPI.Shipped.txt, so a released package that still lists one has changed since its release.
 * @returns {string[]}
 */
export function unshippedApi(root, id) {
  if (kindOf(id) !== PackageKind.NuGet) return [];
  const file = path.join(root, path.dirname(sourceOf(id)), UNSHIPPED_API_FILE);
  if (!existsSync(file)) return [];
  return readFileSync(file, 'utf8')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0 && !line.startsWith(API_FILE_COMMENT));
}

/** Release-mode decision for one package: pack it, skip it (released, unchanged) or refuse it. */
function releaseErrors(entry, unreleased, root) {
  const pending = unreleased.has(entry.id);
  if (entry.inFeed && pending) {
    return [`REFUSED: ${entry.id} ${entry.version} is already in the feed and ${CHANGELOG_FILE} has an [Unreleased] entry for it: bump its version in ${VERSIONS_FILE}.`];
  }
  const unshipped = entry.inFeed ? unshippedApi(root, entry.id) : [];
  if (unshipped.length > 0) {
    return [`REFUSED: ${entry.id} ${entry.version} is already in the feed, but its ${UNSHIPPED_API_FILE} lists ${unshipped.length} public API line(s) (${unshipped[0]}): the source changed after the release. Bump its version in ${VERSIONS_FILE} and add an [Unreleased] entry to ${CHANGELOG_FILE}.`];
  }
  const named = unreleased.get(entry.id);
  if (!entry.inFeed && named && named !== entry.version) {
    return [`REFUSED: ${CHANGELOG_FILE} names ${entry.id} [Unreleased] ${named}, but ${VERSIONS_FILE} says ${entry.version}.`];
  }
  return [];
}

/**
 * The pack plan. mode 'ci' stamps <version>-ci.<ciNumber>; mode 'release' uses the exact version. A missing source is
 * an error. In ci mode a package already in the feed is an error (a feed version is immutable). In release mode a
 * released, unchanged package (in the feed, no [Unreleased] changelog entry, no unshipped public API) gets skip=true
 * and is not packed; one with an [Unreleased] entry or unshipped public API is refused, and a release that would pack
 * nothing is refused.
 * @param {{ root: string, feed: string, mode: string, ciNumber?: number }} options
 */
export function buildPlan({ root, feed, mode, ciNumber = NO_CI_NUMBER }) {
  const { versions, errors } = readVersions(root);
  if (mode === PlanMode.Ci && !(Number.isInteger(ciNumber) && ciNumber >= FIRST_CI_NUMBER)) {
    errors.push(`ci number must be a positive integer (got ${ciNumber}).`);
  }
  const packages = Object.entries(versions).map(([id, baseVersion]) => {
    const version = mode === PlanMode.Release ? baseVersion : `${baseVersion}-${CI_LABEL}.${ciNumber}`;
    const source = sourceOf(id);
    const feedFile = path.join(feedFolderOf(feed, id), feedFileName(id, version));
    const inFeed = isInFeed(feed, id, version);
    return { id, kind: kindOf(id), baseVersion, version, source, feedFile, inFeed, skip: false };
  });
  const unreleased = mode === PlanMode.Release ? readUnreleased(root) : new Map();
  const insecure = readKnownInsecure(root);
  for (const entry of packages) {
    if (insecure.get(entry.id)?.has(entry.baseVersion)) {
      errors.push(`REFUSED: ${CHANGELOG_FILE} marks ${entry.id} ${entry.baseVersion} Known insecure: bump its version in ${VERSIONS_FILE}.`);
    }
    if (!existsSync(path.join(root, entry.source))) errors.push(`MISSING: ${entry.source} (source of ${entry.id}).`);
    if (mode !== PlanMode.Release) {
      if (entry.inFeed) errors.push(`REFUSED: ${entry.id} ${entry.version} is already in the feed (${entry.feedFile}).`);
      continue;
    }
    const refusals = releaseErrors(entry, unreleased, root);
    errors.push(...refusals);
    entry.skip = entry.inFeed && refusals.length === 0;
  }
  if (mode === PlanMode.Release && packages.length > 0 && packages.every((entry) => entry.skip)) {
    errors.push('REFUSED: every package is already released and unchanged; there is nothing to release.');
  }
  return { mode, ciNumber: mode === PlanMode.Release ? null : ciNumber, packages, errors };
}

/** Writes version into an npm package.json (a staged copy: source manifests are never edited). */
export function stampManifest(manifestPath, version) {
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  manifest.version = version;
  writeFileSync(manifestPath, `${JSON.stringify(manifest, null, JSON_INDENT)}\n`);
}

/** Parses `<command> --name value ... [--release]` into { command, options }; null on a malformed line. */
export function parseArguments(argv) {
  const [command, ...rest] = argv;
  if (!command || command.startsWith('--')) return null;
  const options = {};
  for (let index = 0; index < rest.length; index += 1) {
    const name = rest[index];
    if (!name.startsWith('--')) return null;
    if (name === '--release') {
      options.release = true;
      continue;
    }
    const value = rest[index + 1];
    if (value === undefined || value.startsWith('--')) return null;
    options[name.slice('--'.length)] = value;
    index += 1;
  }
  return { command, options };
}

function resolveFeed(options) {
  const root = path.resolve(options.root ?? '.');
  return { root, feed: path.resolve(root, options.feed ?? DEFAULT_FEED) };
}

function runPlan(options) {
  const { root, feed } = resolveFeed(options);
  const mode = options.release ? PlanMode.Release : PlanMode.Ci;
  const plan = buildPlan({ root, feed, mode, ciNumber: Number(options.ci ?? NO_CI_NUMBER) });
  plan.errors.forEach((error) => console.error(error));
  console.log(JSON.stringify(plan, null, JSON_INDENT));
  return plan.errors.length === 0 ? EXIT_OK : EXIT_REFUSED;
}

function runStamp(options) {
  if (!options.manifest || !options.version) return EXIT_USAGE;
  stampManifest(options.manifest, options.version);
  return EXIT_OK;
}

const COMMANDS = Object.freeze({
  plan: runPlan,
  'next-ci': (options) => {
    console.log(nextCiNumber(resolveFeed(options).feed));
    return EXIT_OK;
  },
  stamp: runStamp,
});

export function main(argv) {
  const parsed = parseArguments(argv);
  const run = parsed ? COMMANDS[parsed.command] : undefined;
  const exitCode = run ? run(parsed.options) : EXIT_USAGE;
  if (exitCode === EXIT_USAGE) {
    console.error('Usage: node tools/ci/package-versions.mjs plan --root <path> (--ci <n> | --release) [--feed <dir>]');
    console.error('       node tools/ci/package-versions.mjs next-ci --root <path> [--feed <dir>]');
    console.error('       node tools/ci/package-versions.mjs stamp --manifest <package.json> --version <semver>');
  }
  return exitCode;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  process.exitCode = main(process.argv.slice(2));
}
