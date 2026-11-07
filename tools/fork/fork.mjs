#!/usr/bin/env node
// The fork-model CLI behind the PowerShell entry points (init.ps1, tools/remove-module.ps1,
// tools/upstream-merge.ps1, tools/fork/Test-ForkModel.ps1). Call those scripts; this file is their engine.
//
//   node tools/fork/fork.mjs init --root <repo> --minimal --product-prefix <P> [--remove-module <Name>]...
//        [--feed-dir <dir>] [--rename-map <file>] [--skip-secrets] [--skip-npm-lock] [--allow-missing-targets]
//   node tools/fork/fork.mjs init --root <repo> (--config-file <json> | --interactive) [--what-if] [--skip-database]
//        [--skip-secrets] [--skip-npm-lock] [--feed-dir <dir>]        (the full init, lib/init-steps.mjs)
//   node tools/fork/fork.mjs remove-module --root <repo> --name <Name>
//   node tools/fork/fork.mjs upstream-merge --root <repo> (--upstream <remote|path> [--branch main] | --continue)
//        [--skip-build] [--report <file>]        (free-text build command: env CODELAB_FORK_BUILD_COMMAND)
//   node tools/fork/fork.mjs fork-model --starter-root <repo> --remove-module <Name> [see lib/fork-model.mjs]
//   node tools/fork/fork.mjs add-probe --root <temp copy> --name <Name>        (tools/ci/module-deletability.ps1)
//   node tools/fork/fork.mjs verify-removed --root <temp copy> --name <Name>   (exit 1 lists the remnants)
//
// Exit codes: 0 ok (merged clean), 1 refused / conflicts / build or assertion failed, 2 usage or internal error.
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { OptionKind, parseOptions, UsageError } from './lib/args.mjs';
import { DEFAULT_UPSTREAM_BRANCH, ExitCode, FEED_DIR, JSON_INDENT } from './lib/constants.mjs';
import { FORK_MODEL_OPTIONS, runForkModel } from './lib/fork-model.mjs';
import { runInit } from './lib/init.mjs';
import { readInitConfigFile } from './lib/init-config.mjs';
import { createConsoleAsk, promptInitConfig } from './lib/init-prompts.mjs';
import { loadDependencies, runInitProject } from './lib/init-steps.mjs';
import { addProbe, findRemnants } from './lib/deletability.mjs';
import { ManifestError } from './lib/manifest.mjs';
import { refusalForModuleName } from './lib/modules.mjs';
import { removeModule } from './lib/remove-module.mjs';
import { RefusalError } from './lib/repo.mjs';
import { MergeStatus, runUpstreamMerge } from './lib/upstream-merge.mjs';

const ROOT_FLAG = '--root';
const USAGE = 'Usage: node tools/fork/fork.mjs <init|remove-module|upstream-merge|fork-model|add-probe|verify-removed> [options] (see the header of this file)';

const INIT_OPTIONS = Object.freeze({
  [ROOT_FLAG]: OptionKind.string,
  '--minimal': OptionKind.boolean,
  '--product-prefix': OptionKind.string,
  '--remove-module': OptionKind.list,
  '--feed-dir': OptionKind.string,
  '--rename-map': OptionKind.string,
  '--skip-secrets': OptionKind.boolean,
  '--skip-npm-lock': OptionKind.boolean,
  '--allow-missing-targets': OptionKind.boolean,
  '--config-file': OptionKind.string,
  '--interactive': OptionKind.boolean,
  '--what-if': OptionKind.boolean,
  '--skip-database': OptionKind.boolean,
});
/** Options (camelCase keys) that belong to one init mode only. */
const MINIMAL_ONLY_OPTIONS = Object.freeze(['productPrefix', 'renameMap', 'allowMissingTargets', 'removeModule']);
const FULL_ONLY_OPTIONS = Object.freeze(['configFile', 'interactive', 'whatIf', 'skipDatabase']);
const INTERACTIVE_CONFIG_PREFIX = 'codelab-init-';
const INTERACTIVE_CONFIG_FILE = 'init-config.json';
const NAME_FLAG = '--name';
const REMOVE_MODULE_OPTIONS = Object.freeze({ [ROOT_FLAG]: OptionKind.string, [NAME_FLAG]: OptionKind.string });
/** add-probe and verify-removed take the same options as remove-module. */
const MODULE_OPTIONS = REMOVE_MODULE_OPTIONS;
const UPSTREAM_MERGE_OPTIONS = Object.freeze({
  [ROOT_FLAG]: OptionKind.string,
  '--upstream': OptionKind.string,
  '--branch': OptionKind.string,
  '--continue': OptionKind.boolean,
  '--skip-build': OptionKind.boolean,
  '--report': OptionKind.string,
});

const STATUS_EXIT = Object.freeze({
  [MergeStatus.clean]: ExitCode.ok,
  [MergeStatus.upToDate]: ExitCode.ok,
  [MergeStatus.conflicts]: ExitCode.failed,
  [MergeStatus.buildFailed]: ExitCode.failed,
});

const absolute = (value) => path.resolve(value);

function init(argv, io) {
  const options = parseOptions(argv, INIT_OPTIONS, [ROOT_FLAG]);
  options.root = absolute(options.root);
  options.feedDir = absolute(options.feedDir ?? path.join(options.root, FEED_DIR));
  if (!options.minimal) return initProject(options, io);
  const misplaced = givenOptions(options, FULL_ONLY_OPTIONS);
  if (misplaced.length > 0) throw new UsageError(`--minimal takes none of: ${misplaced.join(', ')}.`);
  if (options.renameMap) options.renameMap = absolute(options.renameMap);
  runInit(options, io);
  return ExitCode.ok;
}

/** The keys among `keys` the caller gave (a true switch, a string or a non-empty list). */
function givenOptions(options, keys) {
  return keys.filter((key) => options[key] === true || typeof options[key] === 'string' || (Array.isArray(options[key]) && options[key].length > 0));
}

/** The interactive answers, kept in a temp file so the continuation after the rename reads the same config. */
async function interactiveConfigFile(options, io) {
  const prompt = createConsoleAsk();
  try {
    const config = await promptInitConfig(prompt.ask, io, { resolve: absolute, skipDatabase: options.skipDatabase });
    const folder = mkdtempSync(path.join(tmpdir(), INTERACTIVE_CONFIG_PREFIX));
    const file = path.join(folder, INTERACTIVE_CONFIG_FILE);
    writeFileSync(file, `${JSON.stringify(config, null, JSON_INDENT)}\n`);
    return { file, cleanup: () => rmSync(folder, { recursive: true, force: true }) };
  } finally {
    prompt.close();
  }
}

/** The full init (init.ps1 without -Minimal): config from --config-file or the prompts, then lib/init-steps.mjs. */
async function initProject(options, io) {
  const misplaced = givenOptions(options, MINIMAL_ONLY_OPTIONS);
  if (misplaced.length > 0) throw new UsageError(`The full init takes none of: ${misplaced.join(', ')} (they belong to --minimal).`);
  if (Boolean(options.configFile) === options.interactive) throw new UsageError('The full init needs exactly one of --config-file and --interactive.');
  const source = options.interactive
    ? await interactiveConfigFile(options, io)
    : { file: absolute(options.configFile), cleanup: () => {} };
  try {
    const checked = readInitConfigFile(source.file);
    if (checked.errors) throw new RefusalError(`The init config is invalid:\n  ${checked.errors.join('\n  ')}`);
    const deps = await loadDependencies();
    return runInitProject({ ...options, config: checked.config, configFile: source.file }, io, deps);
  } finally {
    source.cleanup();
  }
}

function removeModuleCommand(argv, io) {
  const options = parseOptions(argv, REMOVE_MODULE_OPTIONS, [ROOT_FLAG, NAME_FLAG]);
  removeModule(absolute(options.root), options.name, io);
  return ExitCode.ok;
}

function moduleOptions(argv) {
  const options = parseOptions(argv, MODULE_OPTIONS, [ROOT_FLAG, NAME_FLAG]);
  const refusal = refusalForModuleName(options.name, []);
  if (refusal) throw new UsageError(refusal);
  return { root: absolute(options.root), name: options.name };
}

function addProbeCommand(argv, io) {
  const { root, name } = moduleOptions(argv);
  addProbe(root, name, io);
  return ExitCode.ok;
}

function verifyRemovedCommand(argv, io) {
  const { root, name } = moduleOptions(argv);
  const remnants = findRemnants(root, name);
  for (const remnant of remnants) io.error(`  remnant: ${remnant}`);
  if (remnants.length > 0) {
    io.error(`Module ${name} is not fully removed (${remnants.length} remnant(s)).`);
    return ExitCode.failed;
  }
  io.log(`Module ${name} is fully removed: no folder, solution or host line, marker or translation key is left.`);
  return ExitCode.ok;
}

function upstreamMerge(argv, io) {
  const options = parseOptions(argv, UPSTREAM_MERGE_OPTIONS, [ROOT_FLAG]);
  if (!options.continue && !options.upstream) throw new UsageError('--upstream is required (or --continue after resolving conflicts).');
  if (options.continue && options.upstream) throw new UsageError('--continue takes no --upstream.');
  options.root = absolute(options.root);
  options.branch = options.branch ?? DEFAULT_UPSTREAM_BRANCH;
  if (options.report) options.reportPath = absolute(options.report);
  return STATUS_EXIT[runUpstreamMerge(options, io)];
}

function forkModel(argv, io) {
  return runForkModel(parseOptions(argv, FORK_MODEL_OPTIONS.spec, FORK_MODEL_OPTIONS.required), io);
}

const COMMANDS = Object.freeze({
  init,
  'remove-module': removeModuleCommand,
  'upstream-merge': upstreamMerge,
  'fork-model': forkModel,
  'add-probe': addProbeCommand,
  'verify-removed': verifyRemovedCommand,
});

/** The exit code for an error a command threw (or rejected with). */
function exitForError(error, io) {
  if (error instanceof UsageError) {
    io.error(`${error.message}\n${USAGE}`);
    return ExitCode.usage;
  }
  if (error instanceof RefusalError || error instanceof ManifestError) {
    io.error(`REFUSED: ${error.message}`);
    return ExitCode.failed;
  }
  io.error(`ERROR: ${error.stack ?? error.message}`);
  return ExitCode.usage;
}

/** Runs one command; returns the exit code, or a promise of it for the async full init (never throws or rejects). */
export function main(argv, io = { log: (line) => console.log(line), error: (line) => console.error(line) }) {
  const command = COMMANDS[argv[0]];
  if (!command) {
    io.error(USAGE);
    return ExitCode.usage;
  }
  try {
    const result = command(argv.slice(1), io);
    return result instanceof Promise ? result.catch((error) => exitForError(error, io)) : result;
  } catch (error) {
    return exitForError(error, io);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  // Until main settles, an emptied event loop (a promise that never settles) exits as an internal error.
  process.exitCode = ExitCode.usage;
  Promise.resolve(main(process.argv.slice(2))).then((code) => {
    process.exitCode = code;
  });
}
