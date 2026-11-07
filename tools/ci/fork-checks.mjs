#!/usr/bin/env node
// Phase 10 package-consumption check (ADR-002, ADR-003): an init-ed fork consumes the Codelab packages, it never
// builds them. It fails when, in the fork:
//   - packages/ still exists (the package sources must be removed by init);
//   - Directory.Build.props still defaults UseCodelabPackageSource to true (source mode, not feed mode);
//   - a nuget.config lets Codelab.* resolve from anything other than the fork's feed/, a handover kit folder (--kit)
//     or an explicitly allowed hosted feed (--allow-source), or has no <clear/> in packageSources;
//   - an .npmrc points @codelab at a registry that is not allowed;
//   - package-lock.json links an @codelab/* package from source or resolves it from anything other than feed/, a kit
//     folder or an allowed source;
//   - codelab.starter.json is missing, unreadable or has no starterBase.
//
// Usage: node tools/ci/fork-checks.mjs [--root <fork>] [--kit <dir>]... [--allow-source <url prefix>]...
// Exit codes: 0 clean, 1 findings, 2 usage error.

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const EXIT_CLEAN = 0;
export const EXIT_FINDINGS = 1;
export const EXIT_USAGE = 2;

export const PACKAGES_DIR = 'packages';
export const FEED_DIR = 'feed';
export const MANIFEST_FILE = 'codelab.starter.json';
export const BUILD_PROPS_FILE = 'Directory.Build.props';
export const NUGET_CONFIG_NAME = 'nuget.config';
export const NPMRC_FILE = '.npmrc';
export const PACKAGE_JSON_FILE = 'package.json';
export const PACKAGE_LOCK_FILE = 'package-lock.json';
/** A NuGet id that stands for every Codelab.* package when the source mapping is resolved. */
export const CODELAB_NUGET_PROBE_ID = 'Codelab.Probe';
export const CODELAB_NPM_SCOPE = '@codelab';
/** Folders never walked for nuget.config files: git data, build output and installed packages. */
export const SKIPPED_FOLDERS = Object.freeze(['.git', 'node_modules', 'bin', 'obj', 'artifacts', 'publish', 'dist', '.angular', FEED_DIR]);

/** The kinds of finding, also the prefix of every reported line. */
export const FindingKind = Object.freeze({
  PackagesFolder: 'packages-folder',
  SourceMode: 'source-mode',
  NugetSource: 'nuget-source',
  NugetNoClear: 'nuget-no-clear',
  NpmRegistry: 'npm-registry',
  NpmLock: 'npm-lock',
  NpmLockMissing: 'npm-lock-missing',
  Manifest: 'manifest',
});

const ROOT_OPTION = '--root';
const KIT_OPTION = '--kit';
const ALLOW_SOURCE_OPTION = '--allow-source';
const DEFAULT_ROOT = '.';
const ARGUMENTS_START = 2;
const VALUE_OFFSET = 1;
const OPTION_WIDTH = 2;
const EMPTY = 0;
const WILDCARD = '*';
const EXACT_MATCH_SPECIFICITY = Number.POSITIVE_INFINITY;
const NO_MATCH = -1;
const FILE_PROTOCOL = 'file:';
const REMOTE_SOURCE = /^[a-z][a-z0-9+.-]*:\/\//i;
const NODE_MODULES_SEGMENT = 'node_modules/';
const USAGE = `Usage: node tools/ci/fork-checks.mjs [${ROOT_OPTION} <fork>] [${KIT_OPTION} <dir>]... [${ALLOW_SOURCE_OPTION} <url prefix>]...`;

/** True when `child` is `parent` or inside it (case-insensitive on Windows paths). */
export function isInside(parent, child) {
  const relative = path.relative(path.resolve(parent), path.resolve(child));
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

/**
 * True when a package source may serve Codelab packages: a folder inside the fork's feed/ or a kit folder, or a URL
 * that starts with an allowed prefix. `baseDir` is the folder relative paths are resolved from.
 */
export function isAllowedSource(source, { root, baseDir, kitDirs = [], allowedSources = [] }) {
  const value = source.trim();
  if (REMOTE_SOURCE.test(value)) return allowedSources.some((prefix) => value.toLowerCase().startsWith(prefix.toLowerCase()));
  const local = value.toLowerCase().startsWith(FILE_PROTOCOL) ? value.slice(FILE_PROTOCOL.length) : value;
  const absolute = path.resolve(baseDir, local);
  return isInside(path.join(root, FEED_DIR), absolute) || kitDirs.some((kit) => isInside(kit, absolute));
}

function attributes(tag) {
  return Object.fromEntries([...tag.matchAll(/([\w:.-]+)\s*=\s*"([^"]*)"/g)].map((match) => [match[1].toLowerCase(), match[2]]));
}

function section(xml, name) {
  const match = xml.match(new RegExp(`<${name}\\b[^>]*>([\\s\\S]*?)</${name}>`, 'i'));
  return match ? match[1] : undefined;
}

function withoutComments(xml) {
  return xml.replace(/<!--[\s\S]*?-->/g, '');
}

/** Parses a nuget.config: `{ cleared, sources: [{ key, value }], mapping: { key: patterns[] } | undefined }`. */
export function parseNugetConfig(xml) {
  const text = withoutComments(xml);
  const sourcesXml = section(text, 'packageSources') ?? '';
  const sources = [...sourcesXml.matchAll(/<add\b[^>]*>/gi)].map((match) => attributes(match[0]))
    .filter((entry) => entry.key !== undefined && entry.value !== undefined)
    .map((entry) => ({ key: entry.key, value: entry.value }));
  const mappingXml = section(text, 'packageSourceMapping');
  let mapping;
  if (mappingXml !== undefined) {
    mapping = {};
    for (const block of mappingXml.matchAll(/<packageSource\b([^>]*)>([\s\S]*?)<\/packageSource>/gi)) {
      const { key } = attributes(block[1]);
      if (key === undefined) continue;
      mapping[key] = [...block[2].matchAll(/<package\b[^>]*>/gi)].map((tag) => attributes(tag[0]).pattern).filter(Boolean);
    }
  }
  return { cleared: /<clear\s*\/>/i.test(sourcesXml), sources, mapping };
}

/** How specific a source-mapping pattern is for `id`: NO_MATCH, a prefix length, or EXACT_MATCH_SPECIFICITY. */
export function patternSpecificity(pattern, id) {
  const lowerPattern = pattern.toLowerCase();
  const lowerId = id.toLowerCase();
  if (!lowerPattern.endsWith(WILDCARD)) return lowerPattern === lowerId ? EXACT_MATCH_SPECIFICITY : NO_MATCH;
  const prefix = lowerPattern.slice(0, -WILDCARD.length);
  return lowerId.startsWith(prefix) ? prefix.length : NO_MATCH;
}

/** The keys of the sources NuGet may restore `id` from (the most specific mapping pattern wins; no mapping = all). */
export function sourcesServing(config, id) {
  if (config.mapping === undefined) return config.sources.map((source) => source.key);
  const best = Math.max(NO_MATCH, ...Object.values(config.mapping).flatMap((patterns) => patterns.map((pattern) => patternSpecificity(pattern, id))));
  if (best === NO_MATCH) return [];
  return Object.entries(config.mapping)
    .filter(([, patterns]) => patterns.some((pattern) => patternSpecificity(pattern, id) === best))
    .map(([key]) => key);
}

/** Findings for one nuget.config (`file` is root-relative, '/' separators). */
export function checkNugetConfig(xml, file, options) {
  const config = parseNugetConfig(xml);
  const findings = [];
  if (!config.cleared) {
    findings.push({ kind: FindingKind.NugetNoClear, file, message: 'packageSources has no <clear/>: machine and user sources could serve Codelab.*' });
  }
  const baseDir = path.dirname(path.join(options.root, file));
  for (const key of sourcesServing(config, CODELAB_NUGET_PROBE_ID)) {
    const source = config.sources.find((entry) => entry.key === key);
    if (source === undefined) continue;
    if (!isAllowedSource(source.value, { ...options, baseDir })) {
      findings.push({ kind: FindingKind.NugetSource, file, message: `source '${key}' (${source.value}) can serve Codelab.*; only feed/, a handover kit or an allowed feed may` });
    }
  }
  return findings;
}

/** Findings for an .npmrc: a @codelab registry must be an allowed source. */
export function checkNpmrc(text, file, options) {
  const findings = [];
  for (const line of text.split(/\r?\n/)) {
    const match = line.trim().match(/^(@[^:\s]+):registry\s*=\s*(.+)$/i);
    if (!match || match[1].toLowerCase() !== CODELAB_NPM_SCOPE) continue;
    const registry = match[2].trim();
    if (!isAllowedSource(registry, { ...options, baseDir: options.root })) {
      findings.push({ kind: FindingKind.NpmRegistry, file, message: `${CODELAB_NPM_SCOPE} registry '${registry}' is not the feed, a handover kit or an allowed feed` });
    }
  }
  return findings;
}

function isCodelabPackagePath(lockPath) {
  const index = lockPath.lastIndexOf(NODE_MODULES_SEGMENT);
  return index !== NO_MATCH && lockPath.slice(index + NODE_MODULES_SEGMENT.length).toLowerCase().startsWith(`${CODELAB_NPM_SCOPE}/`);
}

/** Findings for package-lock.json: every installed @codelab/* package comes from the feed, a kit or an allowed source. */
export function checkPackageLock(text, file, options) {
  let lock;
  try {
    lock = JSON.parse(text);
  } catch (error) {
    return [{ kind: FindingKind.NpmLock, file, message: `not valid JSON: ${error.message}` }];
  }
  const findings = [];
  for (const [lockPath, entry] of Object.entries(lock.packages ?? {})) {
    if (!isCodelabPackagePath(lockPath)) continue;
    if (entry.link === true) {
      findings.push({ kind: FindingKind.NpmLock, file, message: `${lockPath} is linked from source (${entry.resolved ?? 'workspace'}), not installed from the feed` });
    } else if (typeof entry.resolved !== 'string') {
      findings.push({ kind: FindingKind.NpmLock, file, message: `${lockPath} has no resolved source` });
    } else if (!isAllowedSource(entry.resolved, { ...options, baseDir: path.dirname(path.join(options.root, file)) })) {
      findings.push({ kind: FindingKind.NpmLock, file, message: `${lockPath} resolves from '${entry.resolved}'; only feed/, a handover kit or an allowed feed may serve it` });
    }
  }
  return findings;
}

/** Findings for codelab.starter.json: present, valid JSON and with a non-empty starterBase. */
export function checkManifest(text) {
  if (text === undefined) return [{ kind: FindingKind.Manifest, file: MANIFEST_FILE, message: 'missing: the fork was not init-ed' }];
  let manifest;
  try {
    manifest = JSON.parse(text);
  } catch (error) {
    return [{ kind: FindingKind.Manifest, file: MANIFEST_FILE, message: `not valid JSON: ${error.message}` }];
  }
  const base = manifest?.starterBase;
  if (typeof base !== 'string' || base.trim().length === EMPTY) {
    return [{ kind: FindingKind.Manifest, file: MANIFEST_FILE, message: 'starterBase is missing: init records the starter commit the fork was cut from' }];
  }
  return [];
}

/** Findings for Directory.Build.props: the fork must default to feed mode. */
export function checkBuildProps(text) {
  if (/<UseCodelabPackageSource\b[^>]*>\s*true\s*</i.test(withoutComments(text))) {
    return [{ kind: FindingKind.SourceMode, file: BUILD_PROPS_FILE, message: 'UseCodelabPackageSource defaults to true (source mode); an init-ed fork restores Codelab.* from the feed' }];
  }
  return [];
}

function readIfFile(file) {
  return statSync(file, { throwIfNoEntry: false })?.isFile() ? readFileSync(file, 'utf8') : undefined;
}

/** Root-relative ('/' separators) paths of every nuget.config under `root`, sorted. */
export function listNugetConfigs(root, relative = '') {
  const entries = readdirSync(path.join(root, relative), { withFileTypes: true });
  const found = [];
  for (const entry of entries) {
    const child = relative === '' ? entry.name : `${relative}/${entry.name}`;
    if (entry.isDirectory()) {
      if (!SKIPPED_FOLDERS.includes(entry.name.toLowerCase())) found.push(...listNugetConfigs(root, child));
    } else if (entry.isFile() && entry.name.toLowerCase() === NUGET_CONFIG_NAME) {
      found.push(child);
    }
  }
  return found.sort();
}

/** Runs every check on the fork at `root`; returns `{ kind, file, message }` findings. */
export function checkFork(root, { kitDirs = [], allowedSources = [] } = {}) {
  const options = { root: path.resolve(root), kitDirs: kitDirs.map((kit) => path.resolve(kit)), allowedSources };
  const findings = [];
  if (existsSync(path.join(options.root, PACKAGES_DIR))) {
    findings.push({ kind: FindingKind.PackagesFolder, file: `${PACKAGES_DIR}/`, message: 'the package sources are still in the fork; init removes packages/ (ADR-002)' });
  }
  const buildProps = readIfFile(path.join(options.root, BUILD_PROPS_FILE));
  if (buildProps !== undefined) findings.push(...checkBuildProps(buildProps));
  for (const file of listNugetConfigs(options.root)) {
    findings.push(...checkNugetConfig(readFileSync(path.join(options.root, file), 'utf8'), file, options));
  }
  const npmrc = readIfFile(path.join(options.root, NPMRC_FILE));
  if (npmrc !== undefined) findings.push(...checkNpmrc(npmrc, NPMRC_FILE, options));
  const lock = readIfFile(path.join(options.root, PACKAGE_LOCK_FILE));
  if (lock !== undefined) {
    findings.push(...checkPackageLock(lock, PACKAGE_LOCK_FILE, options));
  } else if (existsSync(path.join(options.root, PACKAGE_JSON_FILE))) {
    findings.push({ kind: FindingKind.NpmLockMissing, file: PACKAGE_LOCK_FILE, message: 'package.json has no lockfile: the @codelab/* sources cannot be verified' });
  }
  findings.push(...checkManifest(readIfFile(path.join(options.root, MANIFEST_FILE))));
  return findings;
}

/** Parses argv (after the script path); returns `{ root, kitDirs, allowedSources }` or `{ error }`. */
export function parseArguments(argv) {
  const parsed = { root: DEFAULT_ROOT, kitDirs: [], allowedSources: [] };
  for (let index = 0; index < argv.length; index += OPTION_WIDTH) {
    const value = argv[index + VALUE_OFFSET];
    if (value === undefined || value.startsWith('--')) return { error: USAGE };
    if (argv[index] === ROOT_OPTION) parsed.root = value;
    else if (argv[index] === KIT_OPTION) parsed.kitDirs.push(value);
    else if (argv[index] === ALLOW_SOURCE_OPTION) parsed.allowedSources.push(value);
    else return { error: USAGE };
  }
  return parsed;
}

/** Runs the check; returns the exit code. */
export function main(argv, log = console.log, logError = console.error) {
  const parsed = parseArguments(argv);
  if (parsed.error) {
    logError(parsed.error);
    return EXIT_USAGE;
  }
  const root = path.resolve(parsed.root);
  if (!statSync(root, { throwIfNoEntry: false })?.isDirectory()) {
    logError(`fork checks: root '${root}' is not a folder.`);
    return EXIT_USAGE;
  }
  const findings = checkFork(root, parsed);
  for (const { kind, file, message } of findings) log(`${kind}: ${file}: ${message}`);
  if (findings.length === EMPTY) {
    log('fork checks: PASSED');
    return EXIT_CLEAN;
  }
  log(`fork checks: FAILED with ${findings.length} finding(s)`);
  return EXIT_FINDINGS;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(ARGUMENTS_START));
}
