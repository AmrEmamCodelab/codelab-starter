#!/usr/bin/env node
// The fork-model CLI behind the PowerShell entry points (init.ps1, tools/remove-module.ps1,
// tools/upstream-merge.ps1, tools/fork/Test-ForkModel.ps1). Call those scripts; this file is their engine.
//
//   node tools/fork/fork.mjs init --root <repo> --minimal --product-prefix <P> [--remove-module <Name>]...
//        [--feed-dir <dir>] [--rename-map <file>] [--skip-secrets] [--skip-npm-lock] [--allow-missing-targets]
//   node tools/fork/fork.mjs remove-module --root <repo> --name <Name>
//   node tools/fork/fork.mjs upstream-merge --root <repo> (--upstream <remote|path> [--branch main] | --continue)
//        [--skip-build] [--report <file>]        (free-text build command: env CODELAB_FORK_BUILD_COMMAND)
//   node tools/fork/fork.mjs fork-model --starter-root <repo> --remove-module <Name> [see lib/fork-model.mjs]
//
// Exit codes: 0 ok (merged clean), 1 refused / conflicts / build or assertion failed, 2 usage or internal error.
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { OptionKind, parseOptions, UsageError } from './lib/args.mjs';
import { DEFAULT_UPSTREAM_BRANCH, ExitCode, FEED_DIR } from './lib/constants.mjs';
import { FORK_MODEL_OPTIONS, runForkModel } from './lib/fork-model.mjs';
import { runInit } from './lib/init.mjs';
import { ManifestError } from './lib/manifest.mjs';
import { removeModule } from './lib/remove-module.mjs';
import { RefusalError } from './lib/repo.mjs';
import { MergeStatus, runUpstreamMerge } from './lib/upstream-merge.mjs';

const ROOT_FLAG = '--root';
const USAGE = 'Usage: node tools/fork/fork.mjs <init|remove-module|upstream-merge|fork-model> [options] (see the header of this file)';

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
});
const REMOVE_MODULE_OPTIONS = Object.freeze({ [ROOT_FLAG]: OptionKind.string, '--name': OptionKind.string });
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
  if (options.renameMap) options.renameMap = absolute(options.renameMap);
  runInit(options, io);
  return ExitCode.ok;
}

function removeModuleCommand(argv, io) {
  const options = parseOptions(argv, REMOVE_MODULE_OPTIONS, [ROOT_FLAG, '--name']);
  removeModule(absolute(options.root), options.name, io);
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

const COMMANDS = Object.freeze({ init, 'remove-module': removeModuleCommand, 'upstream-merge': upstreamMerge, 'fork-model': forkModel });

/** Runs one command; returns the exit code (never throws). */
export function main(argv, io = { log: (line) => console.log(line), error: (line) => console.error(line) }) {
  const command = COMMANDS[argv[0]];
  if (!command) {
    io.error(USAGE);
    return ExitCode.usage;
  }
  try {
    return command(argv.slice(1), io);
  } catch (error) {
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
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main(process.argv.slice(2));
}
