// tools/upstream-merge.ps1: brings starter changes into a fork (ADR-003). It takes the upstream diff since
// starterBase, replays the fork's renameMap on it, drops what lives under removed areas (removedModules, packages/)
// and what the rules engine regenerates, applies the rest with `git apply -3 --index`, reports conflicts, moves
// starterBase, recomputes the hashes and builds.
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { readBlobs, writeBlobs } from './blobs.mjs';
import {
  BUILD_COMMAND_ENV,
  BUILD_SCRIPT,
  BUILD_SCRIPT_ARGUMENTS,
  CONFLICT_MARKER_PATTERN,
  FETCH_HEAD,
  JSON_INDENT,
  MANIFEST_FILE,
  NULL_BLOB,
} from './constants.mjs';
import { isBinaryPatch, parseDiff, serializeDiff, withHunksOf } from './diff.mjs';
import { isBinary } from './hashing.mjs';
import { readManifest, updateManifest } from './manifest.mjs';
import { ConflictKind, createContentTransform, DropReason, isModuleListFile, planPatches } from './merge-plan.mjs';
import { createRemovedPathMatcher, createRemovedProjectLineMatcher } from './modules.mjs';
import { git, gitList, powershellCommand, powershellFile } from './process.mjs';
import { createRenamer } from './rename.mjs';
import { assertCleanGitRoot, compileRulesAndStage, hashTrackedFiles, RefusalError, resolveCommit, trackedFiles } from './repo.mjs';

/** The outcome written to the report and printed. */
export const MergeStatus = Object.freeze({ clean: 'clean', conflicts: 'conflicts', upToDate: 'up-to-date', buildFailed: 'build-failed' });

const TEMP_PREFIX = 'codelab-upstream-merge-';
const PATCH_FILE = 'upstream.patch';
const UTF8 = 'utf8';
const EMPTY = Buffer.alloc(0);
const DIFF_ARGUMENTS = Object.freeze(['diff', '--binary', '--full-index', '--no-renames', '--no-color', '--no-ext-diff', '--no-textconv', '--src-prefix=a/', '--dst-prefix=b/']);

/** Lists the upstream tree (upstream naming) at a commit. */
function upstreamTree(root, commit) {
  return new Set(gitList(root, ['ls-tree', '-r', '-z', '--name-only', commit]));
}

function fetchUpstream(root, upstream, branch) {
  git(root, ['fetch', '--quiet', '--no-tags', upstream, branch]);
  return resolveCommit(root, FETCH_HEAD);
}

function createContext(root, manifest, upstreamCommit) {
  const renamer = createRenamer(manifest.renameMap);
  const forkFiles = new Set(trackedFiles(root));
  const incoming = new Set([...upstreamTree(root, upstreamCommit)].map(renamer.path));
  return {
    renamer,
    forkFiles,
    isRemovedPath: createRemovedPathMatcher(manifest.removedModules),
    hasFile: (file) => forkFiles.has(file) || incoming.has(file),
    removedLineMatcher: createRemovedProjectLineMatcher(manifest.removedModules),
  };
}

function transformBlob(content, forkPath, transform) {
  if (content === undefined || isBinary(content)) return content ?? EMPTY;
  return Buffer.from(transform(forkPath, content.toString(UTF8)), UTF8);
}

/** Regenerates a module-list file's hunks from its transformed blobs (line filtering changes line numbers). */
function regenerateHunks(root, oldId, newId) {
  const output = git(root, ['diff', '--full-index', '--no-color', '--no-ext-diff', oldId, newId]).stdout;
  return parseDiff(output)[0] ?? { hunks: [] };
}

/**
 * Rewrites each kept text patch's content for the fork: writes the transformed pre/post images as blobs (so the
 * three-way fallback merges against renamed content) and, for module-list files, regenerates the hunks.
 */
function rewriteContent(root, kept, context, dropped) {
  const transform = createContentTransform(context.renamer, context.removedLineMatcher);
  const needsRewrite = ({ patch }) => !isBinaryPatch(patch) && patch.oldBlob !== null && (!context.renamer.identity || (isModuleListFile(patch.path) && context.removedLineMatcher));
  const targets = kept.filter(needsRewrite);
  const ids = targets.flatMap(({ patch }) => [patch.oldBlob, patch.newBlob]).filter((id) => id && !NULL_BLOB.test(id));
  const blobs = readBlobs(root, ids);
  const images = targets.flatMap(({ patch }) => [patch.oldBlob, patch.newBlob].map((id) => transformBlob(NULL_BLOB.test(id) ? undefined : blobs.get(id), patch.path, transform)));
  const written = writeBlobs(root, images);
  const result = [];
  for (const entry of kept) {
    const index = targets.indexOf(entry);
    if (index === -1) {
      result.push(entry.patch);
      continue;
    }
    const [oldId, newId] = [written[index * 2], written[index * 2 + 1]];
    let patch = { ...entry.patch, oldBlob: NULL_BLOB.test(entry.patch.oldBlob) ? entry.patch.oldBlob : oldId, newBlob: NULL_BLOB.test(entry.patch.newBlob) ? entry.patch.newBlob : newId };
    if (isModuleListFile(patch.path) && context.removedLineMatcher) {
      patch = withHunksOf(patch, regenerateHunks(root, oldId, newId));
      if (patch.hunks.length === 0) {
        dropped.push({ path: patch.path, upstreamPath: entry.upstreamPath, reason: DropReason.removedModuleLinesOnly });
        continue;
      }
    }
    result.push(patch);
  }
  return result;
}

function unmergedFiles(root) {
  return [...new Set(gitList(root, ['diff', '--name-only', '-z', '--diff-filter=U']))];
}

/** Applies the patches; returns the content-conflicted paths. Throws when git cannot apply them at all. */
function applyPatches(root, patches) {
  if (patches.length === 0) return [];
  const folder = mkdtempSync(path.join(tmpdir(), TEMP_PREFIX));
  try {
    const file = path.join(folder, PATCH_FILE);
    writeFileSync(file, serializeDiff(patches), UTF8);
    const result = git(root, ['apply', '-3', '--index', '--whitespace=nowarn', file], { check: false });
    const conflicted = unmergedFiles(root);
    if (result.code !== 0 && conflicted.length === 0) {
      throw new Error(`git apply could not apply the upstream changes (nothing was applied):\n${result.stderr}`);
    }
    return conflicted;
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
}

function finish(root, io) {
  compileRulesAndStage(root);
  updateManifest(root, (draft) => {
    draft.hashes = hashTrackedFiles(root);
  });
  git(root, ['add', '--', MANIFEST_FILE]);
  io.log('Recomputed codelab.starter.json hashes.');
}

function runBuild(root, options, io) {
  if (options.skipBuild) {
    io.log('Skipped the build (-SkipBuild).');
    return true;
  }
  const command = process.env[BUILD_COMMAND_ENV];
  io.log(command ? `Building: ${command}` : `Building: ${BUILD_SCRIPT} ${BUILD_SCRIPT_ARGUMENTS.join(' ')}`);
  const result = command
    ? powershellCommand(root, command, { check: false, inherit: true })
    : powershellFile(root, path.join(root, BUILD_SCRIPT), [...BUILD_SCRIPT_ARGUMENTS], { check: false, inherit: true });
  return result.code === 0;
}

function writeReport(options, report) {
  if (options.reportPath) writeFileSync(options.reportPath, `${JSON.stringify(report, null, JSON_INDENT)}\n`);
}

function logPlan(io, report) {
  for (const file of report.applied) io.log(`  applied   ${file}`);
  for (const item of report.dropped) io.log(`  dropped   ${item.path} (${item.reason})`);
  for (const item of report.conflicts) io.log(`  CONFLICT  ${item.path} (${item.kind})`);
}

function mergeUpstream(options, io) {
  const manifest = readManifest(options.root);
  if (manifest.starterBase === null) throw new RefusalError('codelab.starter.json has no starterBase: run init.ps1 in this fork first.');
  assertCleanGitRoot(options.root);
  const upstreamCommit = fetchUpstream(options.root, options.upstream, options.branch);
  const report = { status: MergeStatus.upToDate, previousBase: manifest.starterBase, upstreamCommit, applied: [], dropped: [], conflicts: [] };
  if (upstreamCommit === manifest.starterBase) return report;
  const context = createContext(options.root, manifest, upstreamCommit);
  const diff = git(options.root, [...DIFF_ARGUMENTS, manifest.starterBase, upstreamCommit]).stdout;
  const plan = planPatches(parseDiff(diff), context);
  report.dropped = plan.dropped;
  const patches = rewriteContent(options.root, plan.keep, context, report.dropped);
  const conflicted = applyPatches(options.root, patches);
  report.applied = patches.map((patch) => patch.path).filter((file) => !conflicted.includes(file));
  report.conflicts = [...conflicted.map((file) => ({ path: file, kind: ConflictKind.content })), ...plan.conflicts];
  updateManifest(options.root, (draft) => {
    draft.starterBase = upstreamCommit;
  });
  git(options.root, ['add', '--', MANIFEST_FILE]);
  report.status = report.conflicts.length > 0 ? MergeStatus.conflicts : MergeStatus.clean;
  if (report.status === MergeStatus.clean) finish(options.root, io);
  return report;
}

function conflictMarkerFiles(root) {
  const changed = new Set([...gitList(root, ['diff', '--cached', '--name-only', '-z']), ...gitList(root, ['diff', '--name-only', '-z'])]);
  return [...changed].filter((file) => {
    const full = path.join(root, file);
    if (!existsSync(full)) return false;
    const buffer = readFileSync(full);
    return !isBinary(buffer) && CONFLICT_MARKER_PATTERN.test(buffer.toString(UTF8));
  });
}

function continueMerge(options, io) {
  const manifest = readManifest(options.root);
  const unmerged = unmergedFiles(options.root);
  const marked = conflictMarkerFiles(options.root);
  const left = [...new Set([...unmerged, ...marked])];
  const conflicts = left.map((file) => ({ path: file, kind: ConflictKind.content }));
  const report = { status: MergeStatus.conflicts, previousBase: null, upstreamCommit: manifest.starterBase, applied: [], dropped: [], conflicts };
  if (left.length > 0) return report;
  git(options.root, ['add', '-u']);
  finish(options.root, io);
  report.status = MergeStatus.clean;
  return report;
}

/**
 * Runs upstream-merge (or its -Continue step after the human resolved the conflicts).
 * @param {{ root: string, upstream?: string, branch: string, continue: boolean, skipBuild: boolean, reportPath?: string }} options
 * @param {{ log: (line: string) => void }} io
 * @returns {MergeStatus[keyof MergeStatus]}
 */
export function runUpstreamMerge(options, io) {
  const report = options.continue ? continueMerge(options, io) : mergeUpstream(options, io);
  logPlan(io, report);
  if (report.status === MergeStatus.upToDate) {
    io.log(`Already up to date with ${report.upstreamCommit}.`);
  } else if (report.status === MergeStatus.conflicts) {
    io.log(`${report.conflicts.length} conflict(s) left. Resolve them, 'git add' the files, then run tools\\upstream-merge.ps1 -Continue.`);
  } else if (!runBuild(options.root, options, io)) {
    report.status = MergeStatus.buildFailed;
    io.log('The merge applied cleanly but the build failed; fix it before committing.');
  } else {
    io.log('Merged cleanly; review the staged changes and commit them.');
  }
  writeReport(options, report);
  return report.status;
}
