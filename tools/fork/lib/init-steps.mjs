// The full init (init.ps1 without -Minimal; ADR-003): five ordered steps over an init config (init-config.mjs).
//   1 rename    defaultRenameMap({ prefix, nameEn, nameAr }) through the rename engine, prefix targets, branding;
//               records starterBase, renameMap, productPrefix, productName; commit 'chore(init): rename Starter to X'.
//   2 modules   removeOptionalModules(unticked ids), remove packages/, feed mode (pinned versions), npm install, rules;
//               commit 'chore(init): remove modules and packages'.
//   3 keys      tools/devseed/New-LocalSecrets.ps1 -Force [-SkipService ...] (gitignored files, no commit).
//   4 database  tools/devseed/Initialize-LocalDatabase.ps1 -ConnectionString <cs> (no commit).
//   5 manifest  Auth:Modes in the API appsettings; loginModes, optionalModules and (first run) hashes in
//               codelab.starter.json; commit.
// Every step first checks whether it is already done and then skips with a notice, so a rerun continues a partial
// init. Steps 1 and 2 need the npm dependencies (the rules compiler runs on them): on an npm-managed tree (a root
// package-lock.json) without node_modules they are blocked with 'run npm ci first', before anything changes.
// Planning (planInit) is pure; -WhatIf prints the plan and changes nothing. After step 1 renames the tree, the
// remaining steps run in a child node on the renamed tools (their constants name the renamed paths).
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import {
  LOCAL_SECRETS_SCRIPT,
  MANIFEST_FILE,
  NODE_MODULES_DIR,
  NPM_MANIFESTS,
  PACKAGES_DIR,
  RENAME_PROTECTED_NAMES,
  ROOT_NPM_LOCK,
  SOLUTION_TOKEN,
} from './constants.mjs';
import { applyFeedMode, readPackageVersions, resolveFromFeed } from './feed-mode.mjs';
import { reinstallNpm, removePackages, setPrefix } from './init.mjs';
import {
  API_LOCAL_SECRETS_FILE,
  API_SETTINGS_FILE,
  AUTH_MODES_JSON_PATH,
  BRANDING_JSON_PATH,
  DatabaseExit,
  InitCommit,
  LIST_JOINER,
  LOCAL_DATABASE_SCRIPT,
  LOGO_DIR,
  LOGO_STEM,
  LOGO_URL_FOLDER,
  MODULE_REMOVAL_EXPORT,
  MODULE_REMOVAL_MODULE,
  NAME_PLACEHOLDER,
  RENAME_DEFAULTS_EXPORT,
  RENAME_DEFAULTS_MODULE,
  ScriptSwitch,
  skippedSecretServices,
  untickedModules,
  WEB_CONFIG_FILE,
} from './init-config.mjs';
import { readManifest, updateManifest } from './manifest.mjs';
import { refusalForPrefix, setJsonValue } from './prefix.mjs';
import { git, node, powershellFile } from './process.mjs';
import { createRenamer, withProtectedNames } from './rename.mjs';
import { renameTree } from './rename-tree.mjs';
import { assertCleanGitRoot, compileRules, hashTrackedFiles, RefusalError, resolveCommit, workingTreeFiles } from './repo.mjs';

/** The init steps, in run order. */
export const InitStep = Object.freeze({ rename: 1, modules: 2, keys: 3, database: 4, manifest: 5 });
export const STEP_ORDER = Object.freeze([InitStep.rename, InitStep.modules, InitStep.keys, InitStep.database, InitStep.manifest]);
export const STEP_NAMES = Object.freeze({
  [InitStep.rename]: 'rename',
  [InitStep.modules]: 'modules',
  [InitStep.keys]: 'keys',
  [InitStep.database]: 'database',
  [InitStep.manifest]: 'manifest',
});

/** What a planned step does: run it, skip it (done or opted out), or refuse (a precondition is missing). */
export const StepAction = Object.freeze({ run: 1, skip: 2, blocked: 3 });
const ACTION_LABELS = Object.freeze({ [StepAction.run]: 'run', [StepAction.skip]: 'skip', [StepAction.blocked]: 'BLOCKED' });

const HEAD = 'HEAD';
const LIST_SEPARATOR = ', ';
const GIT_DIFF_HAS_CHANGES = 1;
const WHAT_IF_DONE = 'WhatIf: nothing was changed.';
const INIT_DONE = 'Init done.';
const NPM_PREFLIGHT_REFUSAL = `the npm dependencies are not installed (no ${NODE_MODULES_DIR}/); run npm ci first.`;

/** Child-process flags of the continuation run (fork.mjs init). */
export const ContinueFlag = Object.freeze({
  command: 'init',
  root: '--root',
  configFile: '--config-file',
  feedDir: '--feed-dir',
  skipDatabase: '--skip-database',
  skipSecrets: '--skip-secrets',
  skipNpmLock: '--skip-npm-lock',
});
const FORK_CLI = 'tools/fork/fork.mjs';

/**
 * @typedef {{ step: number, action: number, detail: string }} PlannedStep
 * @typedef {{
 *   root: string, config: object, configFile: string, whatIf: boolean, skipDatabase: boolean, skipSecrets: boolean,
 *   skipNpmLock: boolean, feedDir: string,
 * }} InitProjectOptions
 * @typedef {{
 *   defaultRenameMap: ((names: object) => { from: string, to: string }[]) | null,
 *   removeOptionalModules: ((root: string, ids: string[], options: { whatIf: boolean }) => { removed: string[] }) | null,
 *   continueAfterRename: (options: InitProjectOptions) => number,
 * }} InitDependencies
 */

/** Loads one export of a sibling module; null when the module or the export does not exist (yet). */
async function optionalExport(specifier, name) {
  const url = new URL(specifier, import.meta.url);
  if (!existsSync(url)) return null;
  const loaded = await import(url.href);
  return typeof loaded[name] === 'function' ? loaded[name] : null;
}

/** The production dependencies: the contract modules of tasks init-rename and module-removal, and the child run. */
export async function loadDependencies() {
  return {
    defaultRenameMap: await optionalExport(RENAME_DEFAULTS_MODULE, RENAME_DEFAULTS_EXPORT),
    removeOptionalModules: await optionalExport(MODULE_REMOVAL_MODULE, MODULE_REMOVAL_EXPORT),
    continueAfterRename,
  };
}

/** The renamed solution token (the commit message name): the map's entry for it, else the prefix. */
export function renamedSolutionToken(renameMap, prefix) {
  return renameMap.find((entry) => entry.from === SOLUTION_TOKEN)?.to ?? prefix;
}

/** The full renameMap init records: the defaults plus an identity entry per protected manifest name. */
export function plannedRenameMap(defaultRenameMap, config) {
  return withProtectedNames(defaultRenameMap({ prefix: config.prefix, nameEn: config.nameEn, nameAr: config.nameAr }), RENAME_PROTECTED_NAMES);
}

/** The state the plan depends on, read from the repo. Changes nothing. */
export function gatherFacts(options, deps) {
  const { root, config } = options;
  const manifest = readManifest(root);
  const initialized = manifest.starterBase !== null;
  let renameMap = null;
  if (initialized) renameMap = manifest.renameMap;
  else if (deps.defaultRenameMap) renameMap = plannedRenameMap(deps.defaultRenameMap, config);
  const renamer = createRenamer(renameMap ?? []);
  const unticked = untickedModules(config);
  let pendingRemovals = [];
  let removalProblem = null;
  if (unticked.length > 0 && deps.removeOptionalModules) {
    try {
      pendingRemovals = deps.removeOptionalModules(root, unticked, { whatIf: true }).removed;
    } catch (error) {
      if (!(error instanceof RefusalError)) throw error;
      removalProblem = error.message;
    }
  } else if (unticked.length > 0) {
    pendingRemovals = null;
  }
  const packagesPresent = existsSync(path.join(root, PACKAGES_DIR));
  let feedProblem = null;
  if (packagesPresent) {
    try {
      resolveFromFeed(readPackageVersions(root), options.feedDir);
    } catch (error) {
      if (!(error instanceof RefusalError)) throw error;
      feedProblem = error.message;
    }
  }
  const exists = (file) => existsSync(path.join(root, renamer.path(file)));
  const npmManaged = existsSync(path.join(root, ROOT_NPM_LOCK));
  return {
    npmMissing: npmManaged && !existsSync(path.join(root, NODE_MODULES_DIR)),
    starterBase: manifest.starterBase,
    recordedPrefix: manifest.productPrefix,
    renameMap,
    unticked,
    pendingRemovals,
    removalProblem,
    packagesPresent,
    feedProblem,
    secretsScript: exists(LOCAL_SECRETS_SCRIPT),
    secretsExist: exists(API_LOCAL_SECRETS_FILE),
    databaseScript: exists(LOCAL_DATABASE_SCRIPT),
    apiSettingsFile: renamer.path(API_SETTINGS_FILE),
    recordedLoginModes: manifest.loginModes ?? null,
    recordedOptionalModules: manifest.optionalModules ?? null,
    hashesRecorded: Object.keys(manifest.hashes).length > 0,
  };
}

const planned = (step, action, detail) => ({ step, action, detail });

function planRename(facts, config) {
  if (facts.starterBase !== null) {
    const differs = facts.recordedPrefix === config.prefix ? '' : ` (the fork keeps prefix ${facts.recordedPrefix}; the config's ${config.prefix} is ignored)`;
    return planned(InitStep.rename, StepAction.skip, `already done: starterBase ${facts.starterBase}${differs}.`);
  }
  const prefixRefusal = refusalForPrefix(config.prefix);
  if (prefixRefusal) return planned(InitStep.rename, StepAction.blocked, prefixRefusal);
  if (facts.npmMissing) return planned(InitStep.rename, StepAction.blocked, NPM_PREFLIGHT_REFUSAL);
  if (facts.renameMap === null) {
    return planned(InitStep.rename, StepAction.blocked, `tools/fork/lib/${path.basename(RENAME_DEFAULTS_MODULE)} (${RENAME_DEFAULTS_EXPORT}) is missing.`);
  }
  const renames = facts.renameMap.filter((entry) => entry.from !== entry.to).map((entry) => `${entry.from} -> ${entry.to}`);
  const message = InitCommit.rename.replace(NAME_PLACEHOLDER, renamedSolutionToken(facts.renameMap, config.prefix));
  return planned(InitStep.rename, StepAction.run,
    `rename ${renames.join(LIST_SEPARATOR) || 'nothing'}; prefix ${config.prefix}; branding ${config.primaryColor}, `
    + `${config.nameEn} / ${config.nameAr}${config.logoPath ? `, logo ${config.logoPath}` : ''}; commit '${message}'.`);
}

function planModules(facts) {
  if (facts.pendingRemovals === null) {
    return planned(InitStep.modules, StepAction.blocked, `${MODULE_REMOVAL_EXPORT} (tools/fork/lib/${path.basename(MODULE_REMOVAL_MODULE)}) is missing; cannot remove ${facts.unticked.join(LIST_SEPARATOR)}.`);
  }
  if (facts.removalProblem) return planned(InitStep.modules, StepAction.blocked, facts.removalProblem);
  if (!facts.packagesPresent && facts.pendingRemovals.length === 0) {
    return planned(InitStep.modules, StepAction.skip, `already done: no ${PACKAGES_DIR}, no optional module left to remove.`);
  }
  if (facts.feedProblem) return planned(InitStep.modules, StepAction.blocked, facts.feedProblem);
  if (facts.npmMissing) return planned(InitStep.modules, StepAction.blocked, NPM_PREFLIGHT_REFUSAL);
  const parts = [];
  if (facts.pendingRemovals.length > 0) parts.push(`remove module(s) ${facts.pendingRemovals.join(LIST_SEPARATOR)}`);
  if (facts.packagesPresent) parts.push(`remove ${PACKAGES_DIR} and pin the feed versions`);
  return planned(InitStep.modules, StepAction.run, `${parts.join('; ')}; commit '${InitCommit.modules}'.`);
}

function planKeys(facts, config, options) {
  if (options.skipSecrets) return planned(InitStep.keys, StepAction.skip, 'skipped (-SkipSecrets).');
  if (!facts.secretsScript) return planned(InitStep.keys, StepAction.skip, `${LOCAL_SECRETS_SCRIPT} not found; no local keys created.`);
  if (facts.secretsExist) {
    return planned(InitStep.keys, StepAction.skip, `already done: local secrets exist (rerun ${LOCAL_SECRETS_SCRIPT} ${ScriptSwitch.force} to regenerate).`);
  }
  const skipped = skippedSecretServices(config);
  const skip = skipped.length > 0 ? ` ${ScriptSwitch.skipService} ${skipped.join(LIST_JOINER)}` : '';
  return planned(InitStep.keys, StepAction.run, `${LOCAL_SECRETS_SCRIPT} ${ScriptSwitch.force}${skip}.`);
}

function planDatabase(facts, config, options) {
  if (options.skipDatabase || config.database.skip) return planned(InitStep.database, StepAction.skip, 'skipped (-SkipDatabase or database.skip).');
  if (config.database.connectionString === null) return planned(InitStep.database, StepAction.skip, 'skipped: no connection string.');
  if (!facts.databaseScript) return planned(InitStep.database, StepAction.skip, `${LOCAL_DATABASE_SCRIPT} not found; no database initialized.`);
  return planned(InitStep.database, StepAction.run,
    `${LOCAL_DATABASE_SCRIPT} ${ScriptSwitch.connectionString} <given> (migrations, break-glass admin; reports itself when already done).`);
}

function planManifest(facts, config) {
  const recorded = facts.starterBase !== null && facts.hashesRecorded
    && isDeepStrictEqual(facts.recordedLoginModes, config.loginModes)
    && isDeepStrictEqual(facts.recordedOptionalModules, config.optionalModules);
  if (recorded) return planned(InitStep.manifest, StepAction.skip, `already done: ${MANIFEST_FILE} records these login modes and modules.`);
  return planned(InitStep.manifest, StepAction.run,
    `${AUTH_MODES_JSON_PATH} = ${config.loginModes.join(LIST_JOINER)} in ${facts.apiSettingsFile}; ${MANIFEST_FILE} loginModes, optionalModules`
    + `${facts.hashesRecorded ? '' : ', hashes'}; commit '${InitCommit.manifest}'.`);
}

/**
 * The plan: one entry per step in STEP_ORDER. Pure.
 * @returns {PlannedStep[]}
 */
export function planInit(facts, config, options) {
  return [
    planRename(facts, config),
    planModules(facts),
    planKeys(facts, config, options),
    planDatabase(facts, config, options),
    planManifest(facts, config),
  ];
}

/** One plan line: "[run] 1 rename: ...". */
export function formatPlannedStep(entry) {
  return `[${ACTION_LABELS[entry.action]}] ${entry.step} ${STEP_NAMES[entry.step]}: ${entry.detail}`;
}

/** Commits what is staged; false when nothing is. */
function commitStaged(root, message, io) {
  if (git(root, ['diff', '--cached', '--quiet'], { check: false }).code !== GIT_DIFF_HAS_CHANGES) {
    io.log('  nothing to commit.');
    return false;
  }
  git(root, ['commit', '--quiet', '-m', message]);
  io.log(`  committed '${message}'.`);
  return true;
}

function writeJsonValue(root, relativeFile, jsonPath, value, io) {
  const file = path.join(root, relativeFile);
  if (!existsSync(file)) {
    io.log(`  skipped ${relativeFile} (missing).`);
    return false;
  }
  writeFileSync(file, setJsonValue(readFileSync(file, 'utf8'), jsonPath, value).text);
  io.log(`  ${relativeFile} ${jsonPath} written.`);
  return true;
}

/** Copies the logo into the web app; returns its logoSrc ('' without a logo). */
function copyLogo(root, renamer, logoPath) {
  if (logoPath === null) return '';
  const extension = path.extname(logoPath).toLowerCase();
  const folder = path.join(root, renamer.path(LOGO_DIR));
  mkdirSync(folder, { recursive: true });
  copyFileSync(logoPath, path.join(folder, `${LOGO_STEM}${extension}`));
  return `${LOGO_URL_FOLDER}/${LOGO_STEM}${extension}`;
}

function runRename(options, facts, io) {
  const { root, config } = options;
  assertCleanGitRoot(root);
  const starterBase = resolveCommit(root, HEAD);
  const renamer = createRenamer(facts.renameMap);
  const result = renameTree(root, workingTreeFiles(root), renamer);
  io.log(`  renamed ${result.moves.length} path(s) and the content of ${result.contents.length} file(s).`);
  const manifest = readManifest(root);
  const prefixTargets = manifest.prefixTargets.map((target) => ({ ...target, file: renamer.path(target.file) }));
  setPrefix(root, prefixTargets, config.prefix, io);
  const logoSrc = copyLogo(root, renamer, config.logoPath);
  const branding = { primaryColor: config.primaryColor, logoSrc, appName: { ar: config.nameAr, en: config.nameEn } };
  writeJsonValue(root, renamer.path(WEB_CONFIG_FILE), BRANDING_JSON_PATH, branding, io);
  compileRules(root);
  updateManifest(root, (draft) => {
    Object.assign(draft, {
      productPrefix: config.prefix,
      productName: { en: config.nameEn, ar: config.nameAr },
      starterBase,
      renameMap: facts.renameMap,
      prefixTargets,
    });
  });
  git(root, ['add', '-A']);
  commitStaged(root, InitCommit.rename.replace(NAME_PLACEHOLDER, renamedSolutionToken(facts.renameMap, config.prefix)), io);
}

function runModules(options, facts, deps, io) {
  const { root } = options;
  assertCleanGitRoot(root);
  const versions = facts.packagesPresent ? readPackageVersions(root) : null;
  if (facts.pendingRemovals.length > 0) {
    const { removed } = deps.removeOptionalModules(root, facts.unticked, { whatIf: false });
    io.log(`  removed module(s): ${removed.join(LIST_SEPARATOR) || 'none'}.`);
  }
  if (versions !== null) {
    removePackages(root, io);
    const renamer = createRenamer(facts.renameMap ?? []);
    const npmManifests = NPM_MANIFESTS.map((file) => renamer.path(file)).filter((file) => existsSync(path.join(root, file)));
    applyFeedMode(root, { versions, feedDir: options.feedDir, npmManifests }, io);
    reinstallNpm({ root, skipNpmLock: options.skipNpmLock }, io);
  }
  compileRules(root);
  git(root, ['add', '-A']);
  commitStaged(root, InitCommit.modules, io);
}

function runKeys(options, io) {
  const skipped = skippedSecretServices(options.config);
  const args = [ScriptSwitch.force, ...(skipped.length > 0 ? [ScriptSwitch.skipService, skipped.join(LIST_JOINER)] : [])];
  powershellFile(options.root, path.join(options.root, LOCAL_SECRETS_SCRIPT), args, { inherit: true });
}

function runDatabase(options) {
  const script = path.join(options.root, LOCAL_DATABASE_SCRIPT);
  const outcome = powershellFile(options.root, script, [ScriptSwitch.connectionString, options.config.database.connectionString], { inherit: true, check: false });
  if (outcome.code === DatabaseExit.refused) throw new RefusalError(`${LOCAL_DATABASE_SCRIPT} refused (exit ${outcome.code}); see its output.`);
  if (outcome.code !== DatabaseExit.done) throw new Error(`${LOCAL_DATABASE_SCRIPT} failed (exit ${outcome.code}).`);
}

function runManifest(options, facts, io) {
  const { root, config } = options;
  const settingsWritten = writeJsonValue(root, facts.apiSettingsFile, AUTH_MODES_JSON_PATH, config.loginModes, io);
  updateManifest(root, (draft) => {
    draft.loginModes = [...config.loginModes];
    draft.optionalModules = { ...config.optionalModules };
    if (Object.keys(draft.hashes).length === 0) draft.hashes = hashTrackedFiles(root);
  });
  git(root, ['add', '--', MANIFEST_FILE, ...(settingsWritten ? [facts.apiSettingsFile] : [])]);
  commitStaged(root, InitCommit.manifest, io);
}

/** Runs the rest of the init in a child node on the renamed fork's own tools; returns its exit code. */
export function continueAfterRename(options) {
  const args = [
    ContinueFlag.command, ContinueFlag.root, options.root, ContinueFlag.configFile, options.configFile,
    ContinueFlag.feedDir, options.feedDir,
    ...(options.skipDatabase ? [ContinueFlag.skipDatabase] : []),
    ...(options.skipSecrets ? [ContinueFlag.skipSecrets] : []),
    ...(options.skipNpmLock ? [ContinueFlag.skipNpmLock] : []),
  ];
  return node(options.root, path.join(options.root, FORK_CLI), args, { inherit: true, check: false }).code;
}

/**
 * Plans and (unless whatIf) runs the full init. Returns the exit code: 0 done, 1 refused (a blocked step).
 * @param {InitProjectOptions} options
 * @param {{ log: (line: string) => void }} io
 * @param {InitDependencies} deps
 */
export function runInitProject(options, io, deps) {
  const facts = gatherFacts(options, deps);
  const plan = planInit(facts, options.config, options);
  io.log(`Init plan for ${options.root}:`);
  plan.forEach((entry) => io.log(`  ${formatPlannedStep(entry)}`));
  const blocked = plan.filter((entry) => entry.action === StepAction.blocked);
  if (blocked.length > 0) throw new RefusalError(`init cannot run: ${blocked.map((entry) => STEP_NAMES[entry.step]).join(LIST_SEPARATOR)} blocked (see the plan).`);
  if (options.whatIf) {
    io.log(WHAT_IF_DONE);
    return 0;
  }
  for (const entry of plan) {
    if (entry.action === StepAction.skip) continue;
    io.log(`Step ${entry.step} ${STEP_NAMES[entry.step]}`);
    if (entry.step === InitStep.rename) {
      runRename(options, facts, io);
      io.log('Continuing on the renamed tools.');
      return deps.continueAfterRename(options);
    }
    if (entry.step === InitStep.modules) runModules(options, facts, deps, io);
    if (entry.step === InitStep.keys) runKeys(options, io);
    if (entry.step === InitStep.database) runDatabase(options);
    if (entry.step === InitStep.manifest) runManifest(options, facts, io);
  }
  io.log(INIT_DONE);
  return 0;
}
