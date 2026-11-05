#!/usr/bin/env node
// run-local v1: starts the local stack (secrets, LocalDB, optional Seq, the API on :5180 and the web app on :4280),
// health-checks it and prints the URLs and the break-glass user; --stop stops only the PIDs it recorded under
// %TEMP%\codelab-starter. It never installs or deletes anything. run-local.ps1 at the repo root wraps it.
//
// Usage: node tools/run-local/run-local.mjs [--stop] [--no-web] [--no-seq] [--root <dir>]
// Exit codes: 0 ok, 1 refused or failed, 2 usage.
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ExitCode, USAGE } from './lib/constants.mjs';
import { UsageError, parseArguments } from './lib/plan.mjs';
import { main } from './lib/run.mjs';
import { createSystem } from './lib/system.mjs';

const DEFAULT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

async function cli(argv) {
  let options;
  try {
    options = parseArguments(argv, DEFAULT_ROOT);
  } catch (error) {
    if (!(error instanceof UsageError)) throw error;
    console.error(`${error.message}\n${USAGE}`);
    return ExitCode.usage;
  }
  if (options.help) {
    console.log(USAGE);
    return ExitCode.ok;
  }
  return main(options, createSystem());
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = await cli(process.argv.slice(2));
}
