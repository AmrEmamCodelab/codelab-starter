// init.ps1 -Minimal: turns a fresh clone of the starter into a fork in ONE recorded commit (ADR-002, ADR-003).
// Order: validate everything first (nothing changes on a refusal), then remove modules, remove packages/, switch
// to feed mode, apply the renameMap, set the prefix, regenerate the rules outputs, create local secrets, hash and
// write codelab.starter.json, and commit.
import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { UsageError } from './args.mjs';
import {
  BUILD_PROPS_FILE,
  INIT_COMMIT_TEMPLATE,
  LOCAL_SECRETS_SCRIPT,
  NO_REMOVED_MODULES,
  NPM_LOCK_REFRESH_ARGUMENTS,
  NPM_MANIFESTS,
  PACKAGES_DIR,
  PACKAGES_PROPS_FILE,
  REMOVE_MODULE_SCRIPT,
  RENAME_PROTECTED_NAMES,
  ROOT_NPM_LOCK,
  SOLUTION_FILE,
} from './constants.mjs';
import { applyFeedMode, readPackageVersions, resolveFromFeed } from './feed-mode.mjs';
import { readManifest, renameMapErrors, updateManifest } from './manifest.mjs';
import { modulePaths, refusalForModuleName, removeSolutionProjects } from './modules.mjs';
import { refusalForPrefix, setJsonValue } from './prefix.mjs';
import { git, npm, powershellFile } from './process.mjs';
import { createRenamer, withProtectedNames } from './rename.mjs';
import { renameTree } from './rename-tree.mjs';
import { assertCleanGitRoot, compileRules, hashTrackedFiles, RefusalError, resolveCommit, workingTreeFiles } from './repo.mjs';

const HEAD = 'HEAD';
const LIST_SEPARATOR = ', ';

/**
 * @typedef {{
 *   root: string, minimal: boolean, productPrefix?: string, removeModule: string[], feedDir?: string,
 *   renameMap?: string, skipSecrets: boolean, skipNpmLock: boolean, allowMissingTargets: boolean,
 * }} InitOptions
 */

function readRenameMap(file) {
  if (!file) return [];
  if (!existsSync(file)) throw new RefusalError(`Rename map ${file} not found.`);
  let renameMap;
  try {
    renameMap = JSON.parse(readFileSync(file, 'utf8'));
  } catch (error) {
    throw new RefusalError(`Rename map ${file} is not valid JSON: ${error.message}`);
  }
  const errors = renameMapErrors(renameMap);
  if (errors.length > 0) throw new RefusalError(`Rename map ${file} is invalid: ${errors.join(' ')}`);
  return renameMap;
}

/** The optional targets init edits and whether each exists; missing ones need -AllowMissingTargets. */
function optionalTargets(root, manifest) {
  const files = [...manifest.prefixTargets.map((target) => target.file), ...NPM_MANIFESTS];
  return [...new Set(files)].map((file) => ({ file, exists: existsSync(path.join(root, file)) }));
}

function validate(options) {
  if (!options.minimal) throw new UsageError('Only init.ps1 -Minimal is implemented (P2 prototype); the interactive init arrives in P10.');
  const prefixRefusal = refusalForPrefix(options.productPrefix);
  if (prefixRefusal) throw new RefusalError(prefixRefusal);
  const manifest = readManifest(options.root);
  if (manifest.starterBase !== null) {
    throw new RefusalError(`This repo is already an initialized fork (codelab.starter.json starterBase ${manifest.starterBase}).`);
  }
  for (const name of options.removeModule) {
    const refusal = refusalForModuleName(name, manifest.requiredModules);
    if (refusal) throw new RefusalError(refusal);
    const folders = Object.values(modulePaths(name));
    if (!folders.some((folder) => existsSync(path.join(options.root, folder)))) {
      throw new RefusalError(`Module ${name} not found (none of ${folders.join(LIST_SEPARATOR)} exists).`);
    }
  }
  assertCleanGitRoot(options.root);
  for (const required of [BUILD_PROPS_FILE, PACKAGES_PROPS_FILE, SOLUTION_FILE]) {
    if (!existsSync(path.join(options.root, required))) throw new RefusalError(`${required} not found; init edits it.`);
  }
  const missing = optionalTargets(options.root, manifest).filter((target) => !target.exists).map((target) => target.file);
  if (missing.length > 0 && !options.allowMissingTargets) {
    throw new RefusalError(`Missing target file(s): ${missing.join(LIST_SEPARATOR)}. Pass -AllowMissingTargets to skip them explicitly.`);
  }
  const versions = readPackageVersions(options.root);
  resolveFromFeed(versions, options.feedDir);
  return { manifest, versions, missing, renameMap: withProtectedNames(readRenameMap(options.renameMap), RENAME_PROTECTED_NAMES) };
}

function removeModules(options, io) {
  for (const name of options.removeModule) {
    io.log(`Removing module ${name} (${REMOVE_MODULE_SCRIPT})`);
    powershellFile(options.root, path.join(options.root, REMOVE_MODULE_SCRIPT), ['-Name', name], { inherit: true });
  }
}

function removePackages(root, io) {
  const solution = path.join(root, SOLUTION_FILE);
  const result = removeSolutionProjects(readFileSync(solution, 'utf8'), (projectPath) => projectPath.startsWith(PACKAGES_DIR));
  writeFileSync(solution, result.text);
  rmSync(path.join(root, PACKAGES_DIR), { recursive: true, force: true });
  io.log(`Removed ${PACKAGES_DIR} and its ${result.removed.length} solution project(s) (ADR-002).`);
}

function applyRename(root, renameMap, io) {
  const renamer = createRenamer(renameMap);
  if (renamer.identity) return renamer;
  const result = renameTree(root, workingTreeFiles(root), renamer);
  io.log(`Renamed ${result.moves.length} path(s) and the content of ${result.contents.length} file(s) through the renameMap.`);
  return renamer;
}

function setPrefix(root, prefixTargets, prefix, io) {
  for (const target of prefixTargets) {
    const file = path.join(root, target.file);
    if (!existsSync(file)) {
      io.log(`  skipped prefix target ${target.file} (missing; -AllowMissingTargets)`);
      continue;
    }
    const result = setJsonValue(readFileSync(file, 'utf8'), target.jsonPath, prefix);
    writeFileSync(file, result.text);
    io.log(`  ${target.file} ${target.jsonPath} = ${prefix}${result.created ? ' (created)' : ''}`);
  }
}

function refreshNpmLock(options, io) {
  if (options.skipNpmLock) return io.log(`Skipped the ${ROOT_NPM_LOCK} refresh (-SkipNpmLock).`);
  if (!existsSync(path.join(options.root, ROOT_NPM_LOCK))) return io.log(`No ${ROOT_NPM_LOCK}; nothing to refresh.`);
  io.log(`Refreshing ${ROOT_NPM_LOCK} for the feed tarballs`);
  return npm(options.root, [...NPM_LOCK_REFRESH_ARGUMENTS], { inherit: true });
}

function createLocalSecrets(options, io) {
  if (options.skipSecrets) return io.log('Skipped local secrets (-SkipSecrets).');
  const script = path.join(options.root, LOCAL_SECRETS_SCRIPT);
  if (!existsSync(script)) return io.log(`${LOCAL_SECRETS_SCRIPT} does not exist yet; no local secrets were created.`);
  io.log(`Creating local secrets (${LOCAL_SECRETS_SCRIPT})`);
  return powershellFile(options.root, script, [], { inherit: true });
}

function commitMessage(prefix, removed) {
  return INIT_COMMIT_TEMPLATE.replace('{prefix}', prefix).replace('{removed}', removed.length > 0 ? removed.join(LIST_SEPARATOR) : NO_REMOVED_MODULES);
}

/**
 * Runs init -Minimal. Throws RefusalError (nothing changed) when a precondition fails.
 * @param {InitOptions} options feedDir resolved to an absolute path
 * @param {{ log: (line: string) => void }} io
 */
export function runInit(options, io) {
  const checked = validate(options);
  const starterBase = resolveCommit(options.root, HEAD);
  removeModules(options, io);
  removePackages(options.root, io);
  io.log('Switching to feed mode');
  applyFeedMode(options.root, { versions: checked.versions, feedDir: options.feedDir, npmManifests: NPM_MANIFESTS.filter((file) => !checked.missing.includes(file)) }, io);
  const renamer = applyRename(options.root, checked.renameMap, io);
  const prefixTargets = checked.manifest.prefixTargets.map((target) => ({ ...target, file: renamer.path(target.file) }));
  io.log(`Setting the product prefix ${options.productPrefix}`);
  setPrefix(options.root, prefixTargets, options.productPrefix, io);
  compileRules(options.root);
  refreshNpmLock(options, io);
  createLocalSecrets(options, io);
  git(options.root, ['add', '-A']);
  const manifest = updateManifest(options.root, (draft) => {
    Object.assign(draft, { productPrefix: options.productPrefix, starterBase, renameMap: checked.renameMap, prefixTargets });
    draft.hashes = hashTrackedFiles(options.root);
  });
  git(options.root, ['add', '-A']);
  const message = commitMessage(options.productPrefix, manifest.removedModules);
  git(options.root, ['commit', '--quiet', '-m', message]);
  io.log(`Committed '${message}' (starterBase ${starterBase}).`);
  return { starterBase, manifest, message };
}
