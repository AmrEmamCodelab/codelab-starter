// tools/remove-module.ps1 -Name <Name>: removes one module at file level (ADR-004). It edits the solution, the host
// project's ProjectReference list, the lines and blocks of shared files that carry the module's marker comments and
// the module's keys in the translation files; deletes the module's folders; records the removal in
// codelab.starter.json and regenerates the rules outputs. Everything is planned (and validated) before any write.
import { existsSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { API_PROJECT, I18N_FILES, MARKER_SCAN_EXTENSIONS, MARKER_SCAN_ROOTS, MARKER_SCAN_SKIPPED_DIRS, MODULE_MARKER, SOLUTION_FILE } from './constants.mjs';
import { readManifest, updateManifest } from './manifest.mjs';
import { hasModuleMarkers, MarkerError, removeI18nPrefix, stripModuleMarkers, toCamelCase } from './module-markers.mjs';
import { modulePaths, refusalForModuleName, referencesModuleProject, removeProjectReferences, removeSolutionProjects } from './modules.mjs';
import { compileRules, RefusalError } from './repo.mjs';

/** The outcome of a removal. */
export const RemovalStatus = Object.freeze({ removed: 'removed', alreadyRemoved: 'already-removed' });

const EMPTIED_ITEM_GROUP = /^[ \t]*<ItemGroup\b[^>]*>\s*<\/ItemGroup>[ \t]*\r?\n/gm;

function readRequired(root, relativePath) {
  const file = path.join(root, relativePath);
  if (!existsSync(file)) throw new RefusalError(`${relativePath} not found; remove-module edits it and cannot run without it.`);
  return readFileSync(file, 'utf8');
}

function isScannedFile(entryName) {
  return MARKER_SCAN_EXTENSIONS.some((extension) => entryName.endsWith(extension));
}

/**
 * Repo-relative files ('/' separators, sorted) under MARKER_SCAN_ROOTS with a MARKER_SCAN_EXTENSIONS extension,
 * outside MARKER_SCAN_SKIPPED_DIRS and outside the folders in `skippedPrefixes` (the module's own, deleted anyway).
 * @param {string} root
 * @param {readonly string[]} skippedPrefixes repo-relative folders with a trailing '/'
 */
export function listMarkerCandidates(root, skippedPrefixes) {
  const found = [];
  const visit = (relativeDir) => {
    const absolute = path.join(root, relativeDir);
    if (!existsSync(absolute)) return;
    for (const entry of readdirSync(absolute, { withFileTypes: true })) {
      const relative = `${relativeDir}${entry.name}`;
      if (entry.isDirectory()) {
        const folder = `${relative}/`;
        if (!MARKER_SCAN_SKIPPED_DIRS.includes(entry.name) && !skippedPrefixes.some((prefix) => folder.startsWith(prefix))) visit(folder);
      } else if (entry.isFile() && isScannedFile(entry.name)) {
        found.push(relative);
      }
    }
  };
  MARKER_SCAN_ROOTS.forEach(visit);
  return found.sort();
}

/** Runs a pure planning step, turning a MarkerError into a refusal (nothing has been written yet). */
function refuseOnMarkerError(plan) {
  try {
    return plan();
  } catch (error) {
    if (error instanceof MarkerError) throw new RefusalError(error.message);
    throw error;
  }
}

/**
 * Marker stripping of every candidate file that carries the module's markers: [{ file, text, removedLines }].
 * `pending` holds texts already edited in this plan (the host project), so both edits land in one write.
 */
function planMarkerEdits(root, name, skippedPrefixes, pending) {
  const edits = [];
  for (const file of listMarkerCandidates(root, skippedPrefixes)) {
    const original = pending.get(file) ?? readFileSync(path.join(root, file), 'utf8');
    if (!original.includes(MODULE_MARKER.line) || !hasModuleMarkers(original, name)) continue;
    const stripped = refuseOnMarkerError(() => stripModuleMarkers(original, name, file));
    const text = file === API_PROJECT ? stripped.text.replace(EMPTIED_ITEM_GROUP, '') : stripped.text;
    edits.push({ file, text, removedLines: stripped.removedLines });
  }
  return edits;
}

/** Removal of the module's `<camelName>.` keys from each translation file present: [{ file, text, removedKeys }]. */
function planI18nEdits(root, name) {
  const prefix = toCamelCase(name);
  const edits = [];
  for (const file of I18N_FILES.filter((candidate) => existsSync(path.join(root, candidate)))) {
    const result = refuseOnMarkerError(() => removeI18nPrefix(readFileSync(path.join(root, file), 'utf8'), prefix, file));
    if (result.removedKeys.length > 0) edits.push({ file, text: result.text, removedKeys: result.removedKeys });
  }
  return edits;
}

function planRemoval(root, name) {
  const solution = removeSolutionProjects(readRequired(root, SOLUTION_FILE), (projectPath) => referencesModuleProject(projectPath, name));
  const host = removeProjectReferences(readRequired(root, API_PROJECT), (include) => referencesModuleProject(include, name));
  const moduleFolders = Object.values(modulePaths(name));
  const folders = moduleFolders.filter((folder) => existsSync(path.join(root, folder)));
  const markers = planMarkerEdits(root, name, moduleFolders, new Map([[API_PROJECT, host.text]]));
  const i18n = planI18nEdits(root, name);
  /** Every file the removal writes, with its final text (a marker edit of the host project wins over host.text). */
  const writes = new Map([[SOLUTION_FILE, solution.text], [API_PROJECT, host.text]]);
  for (const edit of [...markers, ...i18n]) writes.set(edit.file, edit.text);
  return { solution, host, folders, markers, i18n, writes };
}

function countFindings(plan) {
  return plan.folders.length + plan.solution.removed.length + plan.host.removed.length + plan.markers.length + plan.i18n.length;
}

function applyRemoval(root, plan) {
  for (const [file, text] of plan.writes) writeFileSync(path.join(root, file), text);
  for (const folder of plan.folders) rmSync(path.join(root, folder), { recursive: true, force: true });
}

function logRemoval(plan, io) {
  for (const project of plan.solution.removed) io.log(`  ${SOLUTION_FILE}: removed ${project}`);
  for (const reference of plan.host.removed) io.log(`  ${API_PROJECT}: removed ${reference}`);
  for (const edit of plan.markers) io.log(`  ${edit.file}: removed ${edit.removedLines} marked line(s)`);
  for (const edit of plan.i18n) io.log(`  ${edit.file}: removed key(s) ${edit.removedKeys.join(', ')}`);
  for (const folder of plan.folders) io.log(`  deleted ${folder}`);
}

/**
 * Removes a module from the repo at `root`.
 * @param {string} root
 * @param {string} name PascalCase module name
 * @param {{ log: (line: string) => void }} io
 * @returns {{ status: string, folders: string[], solutionProjects: string[], hostReferences: string[], markerFiles: string[], i18nFiles: string[] }}
 */
export function removeModule(root, name, io) {
  const manifest = readManifest(root);
  const refusal = refusalForModuleName(name, manifest.requiredModules);
  if (refusal) throw new RefusalError(refusal);
  if (manifest.removedModules.includes(name)) {
    io.log(`Module ${name} is already removed (codelab.starter.json removedModules); nothing to do.`);
    return { status: RemovalStatus.alreadyRemoved, folders: [], solutionProjects: [], hostReferences: [], markerFiles: [], i18nFiles: [] };
  }
  const plan = planRemoval(root, name);
  if (countFindings(plan) === 0) throw new RefusalError(`Module ${name} not found: no folder, solution project, host reference, marker or translation key.`);

  applyRemoval(root, plan);
  updateManifest(root, (draft) => {
    draft.removedModules = [...draft.removedModules, name];
  });
  logRemoval(plan, io);
  compileRules(root);
  io.log(`Module ${name} removed and recorded in codelab.starter.json removedModules.`);
  return {
    status: RemovalStatus.removed,
    folders: plan.folders,
    solutionProjects: plan.solution.removed,
    hostReferences: plan.host.removed,
    markerFiles: plan.markers.map((edit) => edit.file),
    i18nFiles: plan.i18n.map((edit) => edit.file),
  };
}
