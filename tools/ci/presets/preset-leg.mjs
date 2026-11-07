#!/usr/bin/env node
// One leg of the CI preset matrix (build plan phase 10): copies the starter into a fresh fork, runs init.ps1 with a
// preset -ConfigFile, checks package consumption and fixture keys, then runs the full CI contract in the fork.
// The workflow (.github/workflows/presets.yml) only calls this script and tools/build.ps1 (ADR-007).
//
// Usage: node tools/ci/presets/preset-leg.mjs (plan|run) --preset <name> [--work <dir>]
//   plan  prints the steps as JSON and changes nothing
//   run   runs the steps in order and stops at the first failure
// Exit codes: 0 passed (or planned), 1 a step failed (it is named), 2 usage error, invalid preset or guard refusal.
//
// It never deletes anything: the fork folder must not exist yet.

import { cpSync, existsSync, readdirSync, readFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';

export const EXIT_PASSED = 0;
export const EXIT_FAILED = 1;
export const EXIT_USAGE = 2;

export const PRESETS_DIR = fileURLToPath(new URL('.', import.meta.url));
export const SOURCE_ROOT = fileURLToPath(new URL('../../..', import.meta.url));
export const PRESET_EXTENSION = '.json';
export const FORK_FOLDER = 'fork';
export const WORK_FOLDER_PREFIX = 'codelab-preset-';
export const FEED_DIR = 'feed';
export const FEED_SUBFOLDERS = Object.freeze(['nuget', 'npm']);
export const FORK_CHECKS_SCRIPT = 'tools/ci/fork-checks.mjs';
export const FIXTURE_KEYS_SCRIPT = 'tools/devseed/Test-NoFixtureKeys.mjs';
export const BUILD_SCRIPT = 'tools/build.ps1';
export const INIT_SCRIPT = 'init.ps1';

/** The init config contract (owned by init-flow; see tools/ci/presets/MODULE.md). */
export const LOGIN_MODES = Object.freeze(['basic', 'ad', 'sso']);
export const OPTIONAL_MODULE_IDS = Object.freeze(['attachments', 'portal', 'sms', 'ops']);
export const PREFIX_PATTERN = /^[A-Z][A-Z0-9]{1,15}$/;
export const RESERVED_PREFIXES = Object.freeze(['STARTER', 'CODELABTEST', 'ALTPFX']);
export const COLOR_PATTERN = /^#[0-9a-f]{6}$/i;
const PRESET_NAME_PATTERN = /^[a-z0-9]+(-[a-z0-9]+)*$/;

/** Step ids, in run order. */
export const StepId = Object.freeze({
  Clone: 'clone',
  Feed: 'feed',
  NpmCi: 'npm-ci',
  Init: 'init',
  ForkChecks: 'fork-checks',
  FixtureKeys: 'fixture-keys',
  Build: 'build',
});

/** How a step runs: a child process, or an in-process folder copy. */
export const StepKind = Object.freeze({ Command: 'command', Copy: 'copy' });

/** The git identity init's recorded commit uses when the runner has none. */
export const CI_GIT_IDENTITY = Object.freeze({
  GIT_AUTHOR_NAME: 'Codelab CI',
  GIT_AUTHOR_EMAIL: 'ci@codelab.invalid',
  GIT_COMMITTER_NAME: 'Codelab CI',
  GIT_COMMITTER_EMAIL: 'ci@codelab.invalid',
});

const POWERSHELL = 'powershell';
const POWERSHELL_PREFIX = Object.freeze(['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File']);
const PLAN_COMMAND = 'plan';
const RUN_COMMAND = 'run';
const PRESET_OPTION = '--preset';
const WORK_OPTION = '--work';
const ARGUMENTS_START = 2;
const OPTIONS_START = 1;
const VALUE_OFFSET = 1;
const OPTION_WIDTH = 2;
const MIN_TEXT_LENGTH = 1;
const JSON_INDENT = 2;
const WINDOWS = 'win32';
const USAGE = `Usage: node tools/ci/presets/preset-leg.mjs (${PLAN_COMMAND}|${RUN_COMMAND}) ${PRESET_OPTION} <name> [${WORK_OPTION} <dir>]`;

/** The preset names (file names without .json) in `presetsDir`, sorted. */
export function listPresets(presetsDir = PRESETS_DIR) {
  return readdirSync(presetsDir).filter((file) => file.endsWith(PRESET_EXTENSION))
    .map((file) => file.slice(0, -PRESET_EXTENSION.length)).sort();
}

function isText(value) {
  return typeof value === 'string' && value.trim().length >= MIN_TEXT_LENGTH;
}

/** Validates a preset against the init -ConfigFile contract; returns the errors (empty when valid). */
export function validatePresetConfig(config) {
  if (config === null || typeof config !== 'object' || Array.isArray(config)) return ['the preset is not a JSON object'];
  const errors = [];
  if (typeof config.prefix !== 'string' || !PREFIX_PATTERN.test(config.prefix)) errors.push('prefix: 2-16 characters, A-Z first, then A-Z or 0-9');
  else if (RESERVED_PREFIXES.includes(config.prefix)) errors.push(`prefix: ${config.prefix} is reserved`);
  if (!isText(config.nameEn)) errors.push('nameEn: required text');
  if (!isText(config.nameAr)) errors.push('nameAr: required text');
  if (typeof config.primaryColor !== 'string' || !COLOR_PATTERN.test(config.primaryColor)) errors.push('primaryColor: #rrggbb');
  if (config.logoPath !== null && !isText(config.logoPath)) errors.push('logoPath: a path or null');
  const modes = config.loginModes;
  if (!Array.isArray(modes) || modes.length < MIN_TEXT_LENGTH) errors.push('loginModes: at least one of basic, ad, sso');
  else if (modes.some((mode) => !LOGIN_MODES.includes(mode)) || new Set(modes).size !== modes.length) errors.push('loginModes: only basic, ad, sso, each once');
  const modules = config.optionalModules;
  if (modules === null || typeof modules !== 'object' || Array.isArray(modules)) {
    errors.push(`optionalModules: an object with ${OPTIONAL_MODULE_IDS.join(', ')}`);
  } else {
    const keys = Object.keys(modules).sort();
    if (keys.join() !== [...OPTIONAL_MODULE_IDS].sort().join() || keys.some((key) => typeof modules[key] !== 'boolean')) {
      errors.push(`optionalModules: exactly ${OPTIONAL_MODULE_IDS.join(', ')}, each true or false`);
    }
  }
  const database = config.database;
  if (database === null || typeof database !== 'object' || typeof database.skip !== 'boolean'
    || !(database.connectionString === null || typeof database.connectionString === 'string')) {
    errors.push('database: { connectionString: string|null, skip: bool }');
  }
  return errors;
}

/** Reads and validates a preset; returns `{ config, file }` or `{ error }`. */
export function readPreset(name, presetsDir = PRESETS_DIR) {
  if (!PRESET_NAME_PATTERN.test(name ?? '') || !listPresets(presetsDir).includes(name)) {
    return { error: `unknown preset '${name}'; known: ${listPresets(presetsDir).join(', ')}` };
  }
  const file = path.join(presetsDir, `${name}${PRESET_EXTENSION}`);
  let config;
  try {
    config = JSON.parse(readFileSync(file, 'utf8'));
  } catch (error) {
    return { error: `${name}${PRESET_EXTENSION} is not valid JSON: ${error.message}` };
  }
  const errors = validatePresetConfig(config);
  return errors.length > 0 ? { error: `${name}${PRESET_EXTENSION}: ${errors.join('; ')}` } : { config, file };
}

function isInside(parent, child) {
  const relative = path.relative(path.resolve(parent), path.resolve(child));
  return relative === '' || (!relative.startsWith('..') && !path.isAbsolute(relative));
}

/** Refuses a work folder that overlaps the source repo or whose fork folder already exists; returns the reason or undefined. */
export function guardWorkDir(sourceRoot, workDir, exists = existsSync) {
  if (isInside(sourceRoot, workDir) || isInside(workDir, sourceRoot)) return `the work folder ${workDir} overlaps the source repo ${sourceRoot}`;
  const fork = path.join(workDir, FORK_FOLDER);
  if (exists(fork)) return `${fork} already exists; pick another --work folder (this script never deletes)`;
  return undefined;
}

function powershellFile(script, args) {
  return { command: POWERSHELL, args: [...POWERSHELL_PREFIX, script, ...args] };
}

/** The steps of one leg (pure: nothing runs). */
export function planLeg({ preset, presetFile, config, sourceRoot = SOURCE_ROOT, workDir }) {
  const fork = path.join(workDir, FORK_FOLDER);
  const initArgs = ['-ConfigFile', presetFile];
  if (config.database.skip) initArgs.push('-SkipDatabase');
  return [
    { id: StepId.Clone, kind: StepKind.Command, cwd: sourceRoot, command: 'git', args: ['clone', '--quiet', '--no-hardlinks', sourceRoot, fork] },
    { id: StepId.Feed, kind: StepKind.Copy, copies: FEED_SUBFOLDERS.map((folder) => ({ from: path.join(sourceRoot, FEED_DIR, folder), to: path.join(fork, FEED_DIR, folder) })) },
    { id: StepId.NpmCi, kind: StepKind.Command, cwd: fork, command: 'npm', args: ['ci', '--no-audit', '--no-fund'] },
    { id: StepId.Init, kind: StepKind.Command, cwd: fork, ...powershellFile(INIT_SCRIPT, initArgs) },
    { id: StepId.ForkChecks, kind: StepKind.Command, cwd: fork, command: process.execPath, args: [path.join(sourceRoot, FORK_CHECKS_SCRIPT), '--root', fork] },
    { id: StepId.FixtureKeys, kind: StepKind.Command, cwd: fork, command: process.execPath, args: [path.join(sourceRoot, FIXTURE_KEYS_SCRIPT), fork] },
    { id: StepId.Build, kind: StepKind.Command, cwd: fork, ...powershellFile(BUILD_SCRIPT, ['-Target', 'CI']) },
  ].map((step) => ({ preset, ...step }));
}

/** The environment of the leg's child processes: the runner's, plus a git identity when it has none. */
export function legEnvironment(base = process.env) {
  const env = { ...base };
  for (const [name, value] of Object.entries(CI_GIT_IDENTITY)) if (!isText(env[name])) env[name] = value;
  return env;
}

function runStep(step, env) {
  if (step.kind === StepKind.Copy) {
    for (const { from, to } of step.copies) if (existsSync(from)) cpSync(from, to, { recursive: true });
    return true;
  }
  // npm is a .cmd shim on Windows: it needs a shell there. Every argument is a fixed word, never user input.
  const result = spawnSync(step.command, step.args, { cwd: step.cwd, env, stdio: 'inherit', shell: step.command === 'npm' && process.platform === WINDOWS });
  return result.status === EXIT_PASSED;
}

/** Runs the steps in order; returns the id of the failed step, or undefined when every step passed. */
export function runLeg(steps, { env = legEnvironment(), run = runStep, log = console.log } = {}) {
  for (const step of steps) {
    log(`preset ${step.preset}: ${step.id}`);
    if (!run(step, env)) return step.id;
  }
  return undefined;
}

/** Parses argv (after the script path); returns `{ command, preset, workDir? }` or `{ error }`. */
export function parseArguments(argv) {
  const [command] = argv;
  if (command !== PLAN_COMMAND && command !== RUN_COMMAND) return { error: USAGE };
  const parsed = { command };
  for (let index = OPTIONS_START; index < argv.length; index += OPTION_WIDTH) {
    const value = argv[index + VALUE_OFFSET];
    if (value === undefined || value.startsWith('--')) return { error: USAGE };
    if (argv[index] === PRESET_OPTION) parsed.preset = value;
    else if (argv[index] === WORK_OPTION) parsed.workDir = value;
    else return { error: USAGE };
  }
  return parsed.preset === undefined ? { error: USAGE } : parsed;
}

/** The default work folder: <RUNNER_TEMP or the OS temp>\codelab-preset-<name>. */
export function defaultWorkDir(preset, env = process.env) {
  return path.join(env.RUNNER_TEMP || os.tmpdir(), `${WORK_FOLDER_PREFIX}${preset}`);
}

/** Runs the CLI; returns the exit code. */
export function main(argv, { log = console.log, logError = console.error, run } = {}) {
  const parsed = parseArguments(argv);
  if (parsed.error) {
    logError(parsed.error);
    return EXIT_USAGE;
  }
  const preset = readPreset(parsed.preset);
  if (preset.error) {
    logError(`preset leg: ${preset.error}`);
    return EXIT_USAGE;
  }
  const workDir = path.resolve(parsed.workDir ?? defaultWorkDir(parsed.preset));
  const refusal = guardWorkDir(SOURCE_ROOT, workDir);
  if (refusal) {
    logError(`preset leg: ${refusal}`);
    return EXIT_USAGE;
  }
  const steps = planLeg({ preset: parsed.preset, presetFile: preset.file, config: preset.config, workDir });
  if (parsed.command === PLAN_COMMAND) {
    log(JSON.stringify(steps, null, JSON_INDENT));
    return EXIT_PASSED;
  }
  const failed = runLeg(steps, { log, ...(run ? { run } : {}) });
  if (failed) {
    log(`preset ${parsed.preset}: FAILED at step '${failed}' (the fork is kept in ${workDir})`);
    return EXIT_FAILED;
  }
  log(`preset ${parsed.preset}: PASSED`);
  return EXIT_PASSED;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(ARGUMENTS_START));
}
