// Decides, per upstream file patch, whether upstream-merge applies, drops or reports it, and how its content is
// rewritten for the fork. Pure: the caller supplies the fork's file set and the removed-area predicates.
import { MODULE_LIST_FILES, MODULE_RULES_FILE, MODULE_RULES_OUTPUTS, RULES_OUTPUTS } from './constants.mjs';
import { renamePatch } from './diff.mjs';
import { removeMatchingLines } from './modules.mjs';

/** Why an upstream file patch was not applied (it is not a conflict). */
export const DropReason = Object.freeze({
  removedArea: 'removed-area',
  alreadyDeleted: 'already-deleted-in-fork',
  regeneratedOutput: 'regenerated-output',
  removedModuleLinesOnly: 'removed-module-lines-only',
});

/** A conflict left for the human. */
export const ConflictKind = Object.freeze({
  content: 'content',
  deletedInFork: 'modified-upstream-deleted-in-fork',
  existsInFork: 'added-upstream-exists-in-fork',
});

const SEPARATOR = '/';

function directoryOf(path) {
  const index = path.lastIndexOf(SEPARATOR);
  return index === -1 ? '' : path.slice(0, index + 1);
}

function baseName(path) {
  return path.slice(path.lastIndexOf(SEPARATOR) + 1);
}

/**
 * True when the path is a rules-engine output: one of the rules/ outputs, or a per-module output next to a
 * module.rules.yaml (`hasFile` answers for the fork's tree or the incoming upstream tree).
 * @param {string} path fork-named, '/' separators
 * @param {(path: string) => boolean} hasFile
 */
export function isRegeneratedOutput(path, hasFile) {
  if (RULES_OUTPUTS.includes(path)) return true;
  if (!MODULE_RULES_OUTPUTS.includes(baseName(path))) return false;
  return hasFile(`${directoryOf(path)}${MODULE_RULES_FILE}`);
}

/** True when the fork-named path lists modules line by line (the solution and the host project). */
export function isModuleListFile(path) {
  return MODULE_LIST_FILES.includes(path);
}

/**
 * The content transform of upstream text for the fork: rename, then (in module list files) drop the lines of
 * removed modules. Applied identically to whole blobs and, through renamePatch, to hunk lines.
 * @param {{ text: (value: string) => string }} renamer
 * @param {((line: string) => boolean) | null} removedLineMatcher
 */
export function createContentTransform(renamer, removedLineMatcher) {
  return (forkPath, text) => {
    const renamed = renamer.text(text);
    return isModuleListFile(forkPath) ? removeMatchingLines(renamed, removedLineMatcher) : renamed;
  };
}

function classify(patch, context) {
  if (context.isRemovedPath(patch.path)) return { drop: DropReason.removedArea };
  if (isRegeneratedOutput(patch.path, context.hasFile)) return { drop: DropReason.regeneratedOutput };
  const inFork = context.forkFiles.has(patch.path);
  if (patch.isNew) return inFork ? { conflict: ConflictKind.existsInFork } : { keep: true };
  if (inFork) return { keep: true };
  return patch.isDeleted ? { drop: DropReason.alreadyDeleted } : { conflict: ConflictKind.deletedInFork };
}

/**
 * Renames every upstream patch into the fork's naming and sorts it into keep, dropped and conflicts.
 * @param {import('./diff.mjs').FilePatch[]} patches upstream naming
 * @param {{
 *   renamer: { text: (v: string) => string, path: (v: string) => string },
 *   isRemovedPath: (path: string) => boolean,
 *   forkFiles: Set<string>,
 *   hasFile: (path: string) => boolean,
 * }} context fork-named predicates
 */
export function planPatches(patches, context) {
  const plan = { keep: [], dropped: [], conflicts: [] };
  for (const upstream of patches) {
    const patch = renamePatch(upstream, context.renamer);
    const decision = classify(patch, context);
    if (decision.keep) plan.keep.push({ patch, upstreamPath: upstream.path });
    else if (decision.drop) plan.dropped.push({ path: patch.path, upstreamPath: upstream.path, reason: decision.drop });
    else plan.conflicts.push({ path: patch.path, kind: decision.conflict });
  }
  return plan;
}
