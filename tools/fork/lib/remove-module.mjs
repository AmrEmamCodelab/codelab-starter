// tools/remove-module.ps1 -Name <Name>: removes one module at file level (ADR-004). It edits the solution, the host
// project's ProjectReference list, the lines and blocks of shared files that carry the module's marker comments and
// the module's keys in the translation files; deletes the module's folders; records the removal in
// codelab.starter.json and regenerates the rules outputs. Everything is planned (and validated) before any write.
import { existsSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import {
  API_PROJECT,
  ExitCode,
  I18N_FILES,
  JSON_INDENT,
  MARKER_SCAN_EXTENSIONS,
  MARKER_SCAN_ROOTS,
  MARKER_SCAN_SKIPPED_DIRS,
  MODULE_MARKER,
  ROOT_NPM_MANIFEST,
  SOLUTION_FILE,
  UTF8_BOM,
} from './constants.mjs';
import { readManifest, updateManifest } from './manifest.mjs';
import { hasModuleMarkers, MarkerError, removeI18nPrefix, stripModuleMarkers, toCamelCase } from './module-markers.mjs';
import {
  isOwnedPath,
  modulePaths,
  normalizeOptionalModuleIds,
  OPTIONAL_MARKER_SCAN,
  OPTIONAL_MODULES,
  optionalModuleOwnedPaths,
  refusalForModuleName,
  referencesModuleProject,
  removeProjectReferences,
  removeSolutionProjects,
} from './modules.mjs';
import { compileRules, RefusalError } from './repo.mjs';

/** The outcome of a removal. */
export const RemovalStatus = Object.freeze({ removed: 'removed', alreadyRemoved: 'already-removed' });

const EMPTIED_ITEM_GROUP = /^[ \t]*<ItemGroup\b[^>]*>\s*<\/ItemGroup>[ \t]*\r?\n/gm;

function readRequired(root, relativePath) {
  const file = path.join(root, relativePath);
  if (!existsSync(file)) throw new RefusalError(`${relativePath} not found; remove-module edits it and cannot run without it.`);
  return readFileSync(file, 'utf8');
}

/**
 * Repo-relative files ('/' separators, sorted) under `scan.roots` with one of `scan.extensions`, outside
 * `scan.skippedDirs` and outside `skippedPaths` (folders with a trailing '/', or files).
 * @param {string} root
 * @param {{ roots: readonly string[], extensions: readonly string[], skippedDirs: readonly string[] }} scan
 * @param {readonly string[]} skippedPaths
 */
export function listScannedFiles(root, scan, skippedPaths) {
  const found = [];
  const visit = (relativeDir) => {
    const absolute = path.join(root, relativeDir);
    if (!existsSync(absolute)) return;
    for (const entry of readdirSync(absolute, { withFileTypes: true })) {
      const relative = `${relativeDir}${entry.name}`;
      if (entry.isDirectory()) {
        const folder = `${relative}/`;
        if (!scan.skippedDirs.includes(entry.name) && !isOwnedPath(folder, skippedPaths)) visit(folder);
      } else if (entry.isFile() && scan.extensions.some((extension) => entry.name.endsWith(extension)) && !isOwnedPath(relative, skippedPaths)) {
        found.push(relative);
      }
    }
  };
  scan.roots.forEach(visit);
  return found.sort();
}

const MODULE_MARKER_SCAN = Object.freeze({ roots: MARKER_SCAN_ROOTS, extensions: MARKER_SCAN_EXTENSIONS, skippedDirs: MARKER_SCAN_SKIPPED_DIRS });

/**
 * Repo-relative files ('/' separators, sorted) under MARKER_SCAN_ROOTS with a MARKER_SCAN_EXTENSIONS extension,
 * outside MARKER_SCAN_SKIPPED_DIRS and outside the folders in `skippedPrefixes` (the module's own, deleted anyway).
 * @param {string} root
 * @param {readonly string[]} skippedPrefixes repo-relative folders with a trailing '/'
 */
export function listMarkerCandidates(root, skippedPrefixes) {
  return listScannedFiles(root, MODULE_MARKER_SCAN, skippedPrefixes);
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

// ---------------------------------------------------------------------------------------------------------------
// Optional modules (init Step 2): removeOptionalModules(repoRoot, ids, { whatIf }) removes whole optional modules
// (OPTIONAL_MODULES in modules.mjs) at file level, never with #if.

const OPTIONAL_MARKER_OPTIONS = Object.freeze({ commentOpeners: OPTIONAL_MARKER_SCAN.commentOpeners });
const WORKSPACES_FIELD = 'workspaces';
const CRLF = '\r\n';
const LINE_BREAK = '\n';
const SILENT_IO = Object.freeze({ log: () => {} });

/**
 * Removes the given workspaces from the root package.json text; returns null when it lists none of them.
 * Keeps the BOM, the indent width JSON_INDENT, the line endings and the final line break.
 * @param {string} text
 * @param {readonly string[]} workspaces
 * @returns {string | null}
 */
export function removeNpmWorkspaces(text, workspaces) {
  if (workspaces.length === 0) return null;
  const bom = text.startsWith(UTF8_BOM) ? UTF8_BOM : '';
  const body = text.slice(bom.length);
  let parsed;
  try {
    parsed = JSON.parse(body);
  } catch (error) {
    throw new RefusalError(`${ROOT_NPM_MANIFEST} is not valid JSON: ${error.message}`);
  }
  const listed = parsed?.[WORKSPACES_FIELD];
  if (!Array.isArray(listed) || !listed.some((entry) => workspaces.includes(entry))) return null;
  parsed[WORKSPACES_FIELD] = listed.filter((entry) => !workspaces.includes(entry));
  const eol = body.includes(CRLF) ? CRLF : LINE_BREAK;
  const finalBreak = body.endsWith(LINE_BREAK) ? eol : '';
  return `${bom}${JSON.stringify(parsed, null, JSON_INDENT).split(LINE_BREAK).join(eol)}${finalBreak}`;
}

function readIfPresent(root, file) {
  const absolute = path.join(root, file);
  return existsSync(absolute) ? readFileSync(absolute, 'utf8') : null;
}

/**
 * Anchor files of the ids that exist but do not carry the id's markers yet, as '<file> (codelab:module <Name>)'.
 * @param {string} root
 * @param {readonly string[]} ids
 * @param {readonly string[]} ownedPaths the paths the removal deletes (an anchor inside them needs no markers)
 */
export function findUnmarkedAnchors(root, ids, ownedPaths) {
  const missing = [];
  for (const id of ids) {
    const { markerName, markerAnchors } = OPTIONAL_MODULES[id];
    for (const anchor of markerAnchors.filter((file) => !isOwnedPath(file, ownedPaths))) {
      const text = readIfPresent(root, anchor);
      if (text !== null && !hasModuleMarkers(text, markerName, OPTIONAL_MARKER_OPTIONS)) missing.push(`${anchor} (${MODULE_MARKER.line} ${markerName})`);
    }
  }
  return missing;
}

/**
 * Every file write of the optional removal besides removeModule's own: marker stripping in the wider scan, the
 * .slnx projects inside owned paths and the root npm workspaces. Pure planning; a MarkerError becomes a refusal.
 * @returns {Map<string, string>} repo-relative file -> new text
 */
function planOptionalWrites(root, ids, ownedPaths) {
  const writes = new Map();
  const current = (file) => writes.get(file) ?? readFileSync(path.join(root, file), 'utf8');
  for (const file of listScannedFiles(root, OPTIONAL_MARKER_SCAN, ownedPaths)) {
    for (const id of ids) {
      const { markerName } = OPTIONAL_MODULES[id];
      const text = current(file);
      if (!hasModuleMarkers(text, markerName, OPTIONAL_MARKER_OPTIONS)) continue;
      writes.set(file, refuseOnMarkerError(() => stripModuleMarkers(text, markerName, file, OPTIONAL_MARKER_OPTIONS)).text);
    }
  }
  if (existsSync(path.join(root, SOLUTION_FILE))) {
    const solution = removeSolutionProjects(current(SOLUTION_FILE), (projectPath) => isOwnedPath(projectPath, ownedPaths));
    if (solution.removed.length > 0) writes.set(SOLUTION_FILE, solution.text);
  }
  const npmManifest = readIfPresent(root, ROOT_NPM_MANIFEST);
  if (npmManifest !== null) {
    const edited = removeNpmWorkspaces(npmManifest, ids.flatMap((id) => OPTIONAL_MODULES[id].npmWorkspaces));
    if (edited !== null) writes.set(ROOT_NPM_MANIFEST, edited);
  }
  return writes;
}

/**
 * Removes optional modules (attachments, portal, sms, ops) from the repo at `repoRoot` at file level: for each id,
 * its backend modules through removeModule (recorded in removedModules), then its other paths, its .slnx projects,
 * its root npm workspaces and every line or block marked `codelab:module <markerName>` in the wider scan
 * (OPTIONAL_MARKER_SCAN). Everything is validated before the first write: an unknown id, unpaired markers or a
 * marker anchor without its markers refuses (RefusalError) with nothing changed. Nothing is staged or committed.
 * @param {string} repoRoot
 * @param {string[]} ids optional module ids (case-insensitive)
 * @param {{ whatIf?: boolean, io?: { log: (line: string) => void } }} [options] whatIf: plan and log, write nothing
 * @returns {{ removed: string[] }} the repo-relative folders (trailing '/') and files removed, or to be removed
 */
export function removeOptionalModules(repoRoot, ids, { whatIf = false, io = SILENT_IO } = {}) {
  const root = path.resolve(repoRoot);
  const normalized = normalizeOptionalModuleIds(ids);
  if (normalized.refusal) throw new RefusalError(normalized.refusal);
  const manifest = readManifest(root);
  const ownedPaths = normalized.ids.flatMap(optionalModuleOwnedPaths);
  const removed = ownedPaths.filter((owned) => existsSync(path.join(root, owned)));
  // An id already removed (none of its paths left) needs no markers: a second run is a no-op, not a refusal.
  const present = normalized.ids.filter((id) => optionalModuleOwnedPaths(id).some((owned) => removed.includes(owned)));
  const unmarked = findUnmarkedAnchors(root, present, ownedPaths);
  if (unmarked.length > 0) {
    throw new RefusalError(`Optional module removal refused, nothing changed. These files must carry the module markers first:\n  ${unmarked.join('\n  ')}`);
  }
  const planned = planOptionalWrites(root, normalized.ids, ownedPaths);
  if (whatIf) {
    for (const file of planned.keys()) io.log(`  would edit ${file}`);
    for (const owned of removed) io.log(`  would delete ${owned}`);
    return { removed };
  }

  for (const name of normalized.ids.flatMap((id) => OPTIONAL_MODULES[id].moduleNames)) {
    if (!manifest.removedModules.includes(name) && existsSync(path.join(root, modulePaths(name).backend))) removeModule(root, name, io);
  }
  for (const [file, text] of planOptionalWrites(root, normalized.ids, ownedPaths)) {
    writeFileSync(path.join(root, file), text);
    io.log(`  edited ${file}`);
  }
  for (const owned of removed) {
    rmSync(path.join(root, owned), { recursive: true, force: true });
    io.log(`  deleted ${owned}`);
  }
  io.log(`Optional module(s) removed: ${normalized.ids.join(', ')}.`);
  return { removed };
}

/** The flags of runOptionalRemovalCli. */
const OptionalCliFlag = Object.freeze({ root: '--root', ids: '--ids', whatIf: '--what-if' });
const LIST_SEPARATOR = ',';
const OPTIONAL_CLI_USAGE = `Usage: ${OptionalCliFlag.root} <repo> ${OptionalCliFlag.ids} <id,id> [${OptionalCliFlag.whatIf}]`;

/**
 * The CLI behind tools/remove-module.ps1 -Optional: --root <repo> --ids a,b [--what-if]. Returns the exit code
 * (ExitCode.ok, ExitCode.failed for a refusal, ExitCode.usage for usage or internal errors).
 * @param {string[]} argv
 * @param {{ log: (line: string) => void, error: (line: string) => void }} io
 */
export function runOptionalRemovalCli(argv, io) {
  const options = { root: null, ids: [], whatIf: false };
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    if (flag === OptionalCliFlag.whatIf) {
      options.whatIf = true;
    } else if (flag === OptionalCliFlag.root) {
      index += 1;
      options.root = argv[index] ?? null;
    } else if (flag === OptionalCliFlag.ids) {
      index += 1;
      options.ids = String(argv[index] ?? '').split(LIST_SEPARATOR).map((id) => id.trim()).filter(Boolean);
    } else {
      io.error(`Unknown option '${flag}'. ${OPTIONAL_CLI_USAGE}`);
      return ExitCode.usage;
    }
  }
  if (!options.root || options.ids.length === 0) {
    io.error(OPTIONAL_CLI_USAGE);
    return ExitCode.usage;
  }
  try {
    removeOptionalModules(options.root, options.ids, { whatIf: options.whatIf, io });
    return ExitCode.ok;
  } catch (error) {
    if (error instanceof RefusalError) {
      io.error(error.message);
      return ExitCode.failed;
    }
    io.error(error?.stack ?? String(error));
    return ExitCode.usage;
  }
}
