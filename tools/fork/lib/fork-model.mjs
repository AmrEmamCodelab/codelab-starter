// tools/fork/Test-ForkModel.ps1: the real-repo fork-model test (ADR-003, the phase 2 exit criterion).
// Snapshot the starter into a scratch upstream (with a scaffolded removable module), clone a fork, init it, change
// upstream (5 edits, 1 add, the removed module, packages/), pre-edit one file in the fork, merge, resolve the one
// expected conflict, then build and test the fork in feed mode and check where Codelab packages came from.
import { copyFileSync, cpSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { OptionKind, UsageError } from './args.mjs';
import {
  BUILD_SCRIPT,
  DOTNET,
  DOTNET_ENV,
  ExitCode,
  FEED_DIR,
  FEED_MODE_PROPERTY,
  FEED_NPM_DIR,
  FEED_NUGET_DIR,
  FORK_MODEL,
  FRONTEND_NPM_MANIFEST,
  INIT_SCRIPT,
  NUGET_PACKAGES_ENV,
  NUPKG_METADATA_FILE,
  OBJ_DIR,
  PACK_SWITCH,
  PACKAGES_DIR,
  PRIVATE_DOTNET,
  PROJECT_ASSETS_FILE,
  ROOT_NPM_LOCK,
  RULES_COMPILE_SCRIPT,
  SCAFFOLD_MODULE_SCRIPT,
  SOLUTION_FILE,
  UPSTREAM_MERGE_SCRIPT,
  VERSIONS_FILE,
} from './constants.mjs';
import { appendProbe, canProbe, checkAssets, checkNpmLock, checkNuGetOrigins, compareSets, isCodelabPackageFolder, resolveByUnion } from './fork-model-checks.mjs';
import { resolveFeedPackages } from './feed.mjs';
import { hashContent } from './hashing.mjs';
import { readManifest } from './manifest.mjs';
import { modulePaths } from './modules.mjs';
import { git, gitList, gitText, node, npm, powershellFile, run } from './process.mjs';

/** CLI options of `fork.mjs fork-model` (Test-ForkModel.ps1 passes them). */
export const FORK_MODEL_OPTIONS = Object.freeze({
  spec: Object.freeze({
    '--starter-root': OptionKind.string,
    '--remove-module': OptionKind.string,
    '--feed-dir': OptionKind.string,
    '--product-prefix': OptionKind.string,
    '--edit-file': OptionKind.list,
    '--conflict-file': OptionKind.string,
    '--add-file': OptionKind.string,
    '--keep-scratch': OptionKind.boolean,
    '--skip-build': OptionKind.boolean,
    '--skip-secrets': OptionKind.boolean,
    '--skip-npm-lock': OptionKind.boolean,
    '--allow-missing-targets': OptionKind.boolean,
  }),
  required: Object.freeze(['--starter-root', '--remove-module']),
});

const UTF8 = 'utf8';
const SCRATCH_REMOVE_RETRIES = 5;
const SCRATCH_REMOVE_RETRY_DELAY_MS = 1000;
/** Environment that turns off MSBuild node reuse and the shared build servers for every child build. */
const BUILD_PROCESS_REUSE_OFF = Object.freeze({ MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_USE_MSBUILD_SERVER: '0', UseSharedCompilation: 'false' });
const JSON_EXTENSION = '.json';

function configureRepo(dir) {
  git(dir, ['config', 'user.name', FORK_MODEL.userName]);
  git(dir, ['config', 'user.email', FORK_MODEL.userEmail]);
}

function commitAll(dir, message) {
  git(dir, ['add', '-A']);
  git(dir, ['commit', '--quiet', '-m', message]);
}

/** a. + b.: the starter's working tree (tracked + untracked, not ignored) plus the scaffolded module, one commit. */
function snapshotUpstream(starterRoot, upstream, moduleName, io) {
  const files = gitList(starterRoot, ['ls-files', '-z', '--cached', '--others', '--exclude-standard']);
  for (const file of files.filter((candidate) => existsSync(path.join(starterRoot, candidate)))) {
    mkdirSync(path.dirname(path.join(upstream, file)), { recursive: true });
    copyFileSync(path.join(starterRoot, file), path.join(upstream, file));
  }
  io.log(`Snapshot: ${files.length} file(s) -> ${upstream}`);
  node(upstream, path.join(upstream, SCAFFOLD_MODULE_SCRIPT), ['--name', moduleName], { inherit: true });
  // The snapshot has no node_modules, so the scaffold skips the rules compile; the starter's own copy runs it.
  node(starterRoot, path.join(starterRoot, RULES_COMPILE_SCRIPT), ['--root', upstream], { inherit: true });
  git(upstream, ['init', '--quiet', '-b', FORK_MODEL.branch]);
  configureRepo(upstream);
  commitAll(upstream, `${FORK_MODEL.commits.snapshot} ${moduleName}`);
}

function feedIsComplete(upstream, feedDir) {
  const versions = JSON.parse(readFileSync(path.join(upstream, VERSIONS_FILE), UTF8));
  const list = (folder) => (existsSync(path.join(feedDir, folder)) ? readdirSync(path.join(feedDir, folder)) : []);
  return resolveFeedPackages(versions, { [FEED_NUGET_DIR]: list(FEED_NUGET_DIR), [FEED_NPM_DIR]: list(FEED_NPM_DIR) }).missing.length === 0;
}

/** When the feed lacks the current versions, packs a prerelease in a scratch copy (never the repo's feed\). */
function ensureFeed(options, scratch, io) {
  if (feedIsComplete(options.upstream, options.feedDir)) return options.feedDir;
  const packRoot = path.join(scratch, FORK_MODEL.scratch.pack);
  io.log(`The feed ${options.feedDir} lacks the current versions; packing a ci prerelease in ${packRoot}`);
  cpSync(options.upstream, packRoot, { recursive: true, filter: (source) => !FORK_MODEL.skippedDirs.includes(path.basename(source)) });
  // The pack's feed-mode check restores the released pins of Directory.Packages.props, so the existing feed comes along.
  if (existsSync(options.feedDir)) cpSync(options.feedDir, path.join(packRoot, FEED_DIR), { recursive: true });
  if (existsSync(path.join(packRoot, ROOT_NPM_LOCK))) npm(packRoot, ['ci', '--no-audit', '--no-fund'], { inherit: true });
  powershellFile(packRoot, path.join(packRoot, BUILD_SCRIPT), [PACK_SWITCH, '-CiNumber', String(FORK_MODEL.ciNumber)], { inherit: true });
  return path.join(packRoot, FEED_DIR);
}

/** c.: clone the fork and run init -Minimal -RemoveModule. */
function createFork(options, fork, feedDir, io) {
  git(path.dirname(fork), ['clone', '--quiet', '--branch', FORK_MODEL.branch, options.upstream, fork]);
  configureRepo(fork);
  if (existsSync(path.join(fork, ROOT_NPM_LOCK))) npm(fork, ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], { inherit: true });
  const args = ['-Minimal', '-ProductPrefix', options.productPrefix, '-RemoveModule', options.removeModule, '-FeedDir', feedDir];
  if (options.skipSecrets) args.push('-SkipSecrets');
  if (options.skipNpmLock) args.push('-SkipNpmLock');
  if (options.allowMissingTargets) args.push('-AllowMissingTargets');
  io.log(`init.ps1 ${args.join(' ')}`);
  powershellFile(fork, path.join(fork, INIT_SCRIPT), args, { inherit: true });
}

function probe(dir, file, label) {
  const full = path.join(dir, file);
  writeFileSync(full, appendProbe(readFileSync(full, UTF8), file, label));
}

function moduleEditTarget(upstream, moduleDir) {
  const candidate = FORK_MODEL.moduleEditCandidates.map((name) => `${moduleDir}${name}`).find((file) => existsSync(path.join(upstream, file)));
  if (candidate) return candidate;
  return gitList(upstream, ['ls-files', '-z', '--', moduleDir]).find(canProbe);
}

/** d.: the upstream commit. Returns the paths it touched, by role. */
function changeUpstream(options, io) {
  const { upstream, editFiles, addFile, removeModule } = options;
  for (const file of editFiles) probe(upstream, file, FORK_MODEL.upstreamLabel);
  mkdirSync(path.dirname(path.join(upstream, addFile)), { recursive: true });
  writeFileSync(path.join(upstream, addFile), `# Fork-model probe\n\nAdded upstream by the fork-model test.\n`);
  const moduleDir = modulePaths(removeModule).backend;
  const moduleEdit = moduleEditTarget(upstream, moduleDir);
  if (moduleEdit) probe(upstream, moduleEdit, FORK_MODEL.upstreamLabel);
  const moduleAdd = `${moduleDir}${FORK_MODEL.moduleProbeFile}`;
  writeFileSync(path.join(upstream, moduleAdd), 'Added upstream inside the removed module.\n');
  mkdirSync(path.join(upstream, PACKAGES_DIR), { recursive: true });
  writeFileSync(path.join(upstream, FORK_MODEL.packagesProbeFile), 'Added upstream under packages/.\n');
  commitAll(upstream, FORK_MODEL.commits.upstream);
  io.log(`Upstream commit: edited ${editFiles.join(', ')}; added ${addFile}; touched ${[moduleEdit, moduleAdd].filter(Boolean).join(', ')} and ${FORK_MODEL.packagesProbeFile}`);
  return { removedTouched: [moduleEdit, moduleAdd, FORK_MODEL.packagesProbeFile].filter(Boolean) };
}

function runMerge(fork, args, reportPath) {
  const result = powershellFile(fork, path.join(fork, UPSTREAM_MERGE_SCRIPT), [...args, '-SkipBuild', '-ReportPath', reportPath], { check: false, inherit: true });
  return { code: result.code, report: existsSync(reportPath) ? JSON.parse(readFileSync(reportPath, UTF8)) : null };
}

/** e. + f.: pre-edit, merge, assert the expected conflict, resolve it scripted and continue. */
function mergeIntoFork(options, fork, scratch, failures) {
  probe(fork, options.conflictFile, FORK_MODEL.forkLabel);
  commitAll(fork, FORK_MODEL.commits.forkEdit);
  const first = runMerge(fork, ['-Upstream', options.upstream, '-Branch', FORK_MODEL.branch], path.join(scratch, FORK_MODEL.scratch.mergeReport));
  if (!first.report) return failures.push(`upstream-merge wrote no report (exit ${first.code}).`);
  const conflicts = compareSets([options.conflictFile], first.report.conflicts.map((item) => item.path));
  if (conflicts.missing.length + conflicts.unexpected.length > 0) failures.push(`Conflicts differ: missing ${conflicts.missing}, unexpected ${conflicts.unexpected}.`);
  if (first.code !== ExitCode.failed) failures.push(`upstream-merge exit ${first.code}, expected ${ExitCode.failed} (conflicts).`);
  const conflictPath = path.join(fork, options.conflictFile);
  writeFileSync(conflictPath, resolveByUnion(readFileSync(conflictPath, UTF8)));
  git(fork, ['add', '--', options.conflictFile]);
  const second = runMerge(fork, ['-Continue'], path.join(scratch, FORK_MODEL.scratch.continueReport));
  if (second.code !== ExitCode.ok) failures.push(`upstream-merge -Continue exit ${second.code}, expected ${ExitCode.ok}.`);
  return first.report;
}

function checkMergeResult(options, fork, report, touched, failures) {
  const manifest = readManifest(fork);
  const head = gitText(options.upstream, ['rev-parse', 'HEAD']);
  if (manifest.starterBase !== head) failures.push(`starterBase is ${manifest.starterBase}, expected the upstream head ${head}.`);
  const applied = compareSets([...options.editFiles.filter((file) => file !== options.conflictFile), options.addFile], report?.applied ?? []);
  if (applied.missing.length > 0) failures.push(`Not applied: ${applied.missing.join(', ')}.`);
  const dropped = new Set((report?.dropped ?? []).map((item) => item.upstreamPath));
  for (const file of touched.removedTouched) if (!dropped.has(file)) failures.push(`Not dropped: ${file}.`);
  for (const file of [options.conflictFile, options.addFile]) {
    const full = path.join(fork, file);
    if (!existsSync(full) || manifest.hashes[file] !== hashContent(readFileSync(full))) failures.push(`hashes['${file}'] was not recomputed.`);
  }
  if (existsSync(path.join(fork, PACKAGES_DIR))) failures.push('The fork has a packages/ folder.');
  if (existsSync(path.join(fork, modulePaths(options.removeModule).backend))) failures.push(`The fork has the removed module ${options.removeModule}.`);
}

function resolveDotnet() {
  if (process.env[DOTNET_ENV]) return process.env[DOTNET_ENV];
  return existsSync(PRIVATE_DOTNET) ? PRIVATE_DOTNET : DOTNET;
}

function findAssets(dir, found = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (!entry.isDirectory() || FORK_MODEL.skippedDirs.includes(entry.name)) continue;
    const child = path.join(dir, entry.name);
    if (entry.name === OBJ_DIR && existsSync(path.join(child, PROJECT_ASSETS_FILE))) found.push(path.join(child, PROJECT_ASSETS_FILE));
    else findAssets(child, found);
  }
  return found;
}

function restoredCodelabPackages(packagesFolder) {
  return readdirSync(packagesFolder).filter(isCodelabPackageFolder).flatMap((id) => readdirSync(path.join(packagesFolder, id)).map((version) => {
    const file = path.join(packagesFolder, id, version, NUPKG_METADATA_FILE);
    return { id, version, metadata: existsSync(file) ? JSON.parse(readFileSync(file, UTF8)) : {} };
  }));
}

/** g. + h.: feed-mode build and test of the fork, then the feed-origin checks. */
function buildFork(fork, scratch, failures, io) {
  const packagesFolder = path.join(scratch, FORK_MODEL.scratch.nugetPackages);
  mkdirSync(packagesFolder, { recursive: true });
  const env = { ...process.env, [NUGET_PACKAGES_ENV]: packagesFolder };
  const dotnet = resolveDotnet();
  for (const verb of ['build', 'test']) {
    const result = run(dotnet, [verb, SOLUTION_FILE, FEED_MODE_PROPERTY], { cwd: fork, env, check: false, inherit: true });
    if (result.code !== 0) failures.push(`dotnet ${verb} ${SOLUTION_FILE} (feed mode) failed with exit ${result.code}.`);
  }
  const feedNuGet = path.join(fork, FEED_DIR, FEED_NUGET_DIR);
  for (const assets of findAssets(fork)) failures.push(...checkAssets(JSON.parse(readFileSync(assets, UTF8)), feedNuGet, path.relative(fork, assets)));
  failures.push(...checkNuGetOrigins(restoredCodelabPackages(packagesFolder), feedNuGet));
  const frontend = path.dirname(FRONTEND_NPM_MANIFEST);
  // npm ci, not install: init removes packages/, which leaves dangling workspace links that install keeps.
  for (const args of [['ci', '--no-audit', '--no-fund'], ['run', 'build', '--workspace', frontend], ['test', '--workspace', frontend]]) {
    const result = npm(fork, args, { check: false, inherit: true });
    if (result.code !== 0) failures.push(`npm ${args.join(' ')} failed with exit ${result.code}.`);
  }
  const lock = path.join(fork, ROOT_NPM_LOCK);
  if (existsSync(lock)) failures.push(...checkNpmLock(JSON.parse(readFileSync(lock, UTF8)), `${FEED_DIR}/${FEED_NPM_DIR}`));
  io.log('Feed-mode build and origin checks done.');
}

function validateOptions(options) {
  if (!existsSync(path.join(options.starterRoot, SCAFFOLD_MODULE_SCRIPT))) {
    throw new UsageError(`${SCAFFOLD_MODULE_SCRIPT} not found in ${options.starterRoot}: the fork-model test scaffolds the removable module with it (the scaffolder arrives in phase 2 wave 3).`);
  }
  if (options.editFiles.length !== FORK_MODEL.editCount) throw new UsageError(`Pass exactly ${FORK_MODEL.editCount} --edit-file values.`);
  const unprobeable = options.editFiles.filter((file) => !canProbe(file) || file.endsWith(JSON_EXTENSION));
  if (unprobeable.length > 0) throw new UsageError(`No probe comment style for: ${unprobeable.join(', ')}.`);
  if (!options.editFiles.includes(options.conflictFile)) throw new UsageError('--conflict-file must be one of the edit files.');
}

function withDefaults(raw) {
  const editFiles = raw.editFile.length > 0 ? raw.editFile : [...FORK_MODEL.editFiles];
  return {
    ...raw,
    starterRoot: path.resolve(raw.starterRoot),
    editFiles,
    conflictFile: raw.conflictFile ?? editFiles[0],
    addFile: raw.addFile ?? FORK_MODEL.addFile,
    productPrefix: raw.productPrefix ?? FORK_MODEL.productPrefix,
  };
}

/**
 * Runs the fork-model test; returns ExitCode.ok when every assertion holds, ExitCode.failed otherwise.
 * @param {Record<string, unknown>} raw parsed FORK_MODEL_OPTIONS
 * @param {{ log: (line: string) => void, error: (line: string) => void }} io
 */
export function runForkModel(raw, io) {
  const options = withDefaults(raw);
  validateOptions(options);
  // Reused MSBuild nodes and the build server keep task DLLs of the scratch builds locked, which blocks the cleanup.
  Object.assign(process.env, BUILD_PROCESS_REUSE_OFF);
  const scratch = mkdtempSync(path.join(tmpdir(), FORK_MODEL.tempPrefix));
  const failures = [];
  try {
    options.upstream = path.join(scratch, FORK_MODEL.scratch.upstream);
    snapshotUpstream(options.starterRoot, options.upstream, options.removeModule, io);
    const feedDir = ensureFeed({ ...options, feedDir: path.resolve(raw.feedDir ?? path.join(options.starterRoot, FEED_DIR)) }, scratch, io);
    const fork = path.join(scratch, FORK_MODEL.scratch.fork);
    createFork(options, fork, feedDir, io);
    const touched = changeUpstream(options, io);
    const report = mergeIntoFork(options, fork, scratch, failures);
    checkMergeResult(options, fork, report, touched, failures);
    git(fork, ['commit', '--quiet', '-m', FORK_MODEL.commits.merge]);
    if (options.skipBuild) io.log('Skipped the feed-mode build and test (-SkipBuild).');
    else buildFork(fork, scratch, failures, io);
  } finally {
    if (options.keepScratch) io.log(`Scratch kept: ${scratch}`);
    else removeScratch(scratch, io);
  }
  for (const failure of failures) io.error(`FAIL: ${failure}`);
  io.log(failures.length === 0 ? 'Fork-model test PASSED.' : `Fork-model test FAILED (${failures.length}).`);
  return failures.length === 0 ? ExitCode.ok : ExitCode.failed;
}

/** Removes the scratch folder; a file still locked by another process leaves a warning, never a failed test. */
function removeScratch(scratch, io) {
  try {
    rmSync(scratch, { recursive: true, force: true, maxRetries: SCRATCH_REMOVE_RETRIES, retryDelay: SCRATCH_REMOVE_RETRY_DELAY_MS });
  } catch (error) {
    io.error(`WARNING: could not remove the scratch folder ${scratch} (${error.message}); delete it after 'dotnet build-server shutdown'.`);
  }
}

/** Exposed for the unit tests. */
export const internals = Object.freeze({ withDefaults, validateOptions });
