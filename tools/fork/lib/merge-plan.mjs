// Decides, per upstream file patch, whether upstream-merge applies, drops or reports it, and how its content is
// rewritten for the fork. Pure: the caller supplies the fork's file set and the removed-area predicates.
import {
  I18N_FILES,
  MANIFEST_FILE,
  MARKER_SCAN_EXTENSIONS,
  MARKER_SCAN_ROOTS,
  MODULE_LIST_FILES,
  MODULE_RULES_FILE,
  MODULE_RULES_OUTPUTS,
  RULES_OUTPUTS,
  UTF8_BOM,
} from './constants.mjs';
import { renamePatch } from './diff.mjs';
import { MarkerError, removeI18nPrefix, stripModuleMarkers, toCamelCase } from './module-markers.mjs';
import { removeMatchingLines } from './modules.mjs';

/** Why an upstream file patch was not applied (it is not a conflict). */
export const DropReason = Object.freeze({
  removedArea: 'removed-area',
  alreadyDeleted: 'already-deleted-in-fork',
  regeneratedOutput: 'regenerated-output',
  removedModuleLinesOnly: 'removed-module-lines-only',
  forkManifest: 'fork-manifest',
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

/** True when remove-module strips module marker lines from the fork-named path (a shared registration file). */
export function isMarkerFile(path) {
  return MARKER_SCAN_ROOTS.some((root) => path.startsWith(root)) && MARKER_SCAN_EXTENSIONS.some((extension) => path.endsWith(extension));
}

/** True when the fork-named path is a translation file (remove-module deletes the module's keys from it). */
export function isI18nFile(path) {
  return I18N_FILES.includes(path);
}

/** True when the removed modules' registrations are filtered out of the file's upstream content. */
export function isModuleFilteredFile(path) {
  return isModuleListFile(path) || isMarkerFile(path) || isI18nFile(path);
}

/** Runs one strip step; text it cannot parse (unpaired markers, invalid JSON) stays as upstream wrote it. */
function stripOrKeep(text, strip) {
  try {
    return strip(text);
  } catch (error) {
    if (error instanceof MarkerError) return text;
    throw error;
  }
}

/** Removes the removed modules' marked lines/blocks and translation keys, as remove-module did in the fork. */
function stripRemovedModules(forkPath, text, removedModules) {
  let result = text;
  if (isMarkerFile(forkPath)) {
    for (const name of removedModules) result = stripOrKeep(result, (value) => stripModuleMarkers(value, name, forkPath).text);
  }
  if (isI18nFile(forkPath)) {
    for (const name of removedModules) result = stripOrKeep(result, (value) => removeI18nPrefix(value, toCamelCase(name), forkPath).text);
  }
  return result;
}

/**
 * The content transform of upstream text for the fork: rename, then drop the removed modules' registrations (their
 * project lines in the module list files, their marked lines in shared files, their translation keys). Applied to
 * whole blobs; the caller regenerates the hunks of module-filtered files from the transformed blobs.
 * @param {{ text: (value: string) => string }} renamer
 * @param {((line: string) => boolean) | null} removedLineMatcher
 * @param {readonly string[]} [removedModules] PascalCase module names
 */
export function createContentTransform(renamer, removedLineMatcher, removedModules = []) {
  return (forkPath, text) => {
    const renamed = renamer.text(text);
    const listed = isModuleListFile(forkPath) ? removeMatchingLines(renamed, removedLineMatcher) : renamed;
    return removedModules.length > 0 ? stripRemovedModules(forkPath, listed, removedModules) : listed;
  };
}

/**
 * The starter-owned fields a fork takes from the upstream manifest when starterBase moves; the rest of
 * codelab.starter.json belongs to the fork. Returns {} for a missing or unreadable upstream manifest.
 * @param {string | null} upstreamManifestText
 * @returns {{ starterVersion?: string }}
 */
export function starterFieldsFrom(upstreamManifestText) {
  if (!upstreamManifestText) return {};
  try {
    const parsed = JSON.parse(upstreamManifestText.startsWith(UTF8_BOM) ? upstreamManifestText.slice(UTF8_BOM.length) : upstreamManifestText);
    return typeof parsed?.starterVersion === 'string' ? { starterVersion: parsed.starterVersion } : {};
  } catch {
    return {};
  }
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
    // The fork owns its codelab.starter.json (never renamed); upstream-merge takes the starter fields itself.
    if (upstream.path === MANIFEST_FILE) {
      plan.dropped.push({ path: MANIFEST_FILE, upstreamPath: upstream.path, reason: DropReason.forkManifest });
      continue;
    }
    const patch = renamePatch(upstream, context.renamer);
    const decision = classify(patch, context);
    if (decision.keep) plan.keep.push({ patch, upstreamPath: upstream.path });
    else if (decision.drop) plan.dropped.push({ path: patch.path, upstreamPath: upstream.path, reason: decision.drop });
    else plan.conflicts.push({ path: patch.path, kind: decision.conflict });
  }
  return plan;
}
