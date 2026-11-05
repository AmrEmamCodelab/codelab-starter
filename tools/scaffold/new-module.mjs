#!/usr/bin/env node
// Scaffolds a backend module (ADR-004) with its tests, lists it in the solution and the host project, then reruns
// the rules compile and DocsGen. Prints every file it created or changed.
//
// Usage: node tools/scaffold/new-module.mjs --name <Pascal> [--root <dir>] [--skip-generate]
// Exit codes: 0 done, 1 refused (nothing changed), 2 usage or a failed downstream generator.
import { pathToFileURL } from 'node:url';
import { Flags } from './lib/constants.mjs';
import { UsageError, parseArguments, runCli } from './lib/cli.mjs';
import { planModule } from './lib/scaffold.mjs';
import { ChangeSet, existingModules, regenerate, removedModules } from './lib/workspace.mjs';

const USAGE = `Usage: node tools/scaffold/new-module.mjs ${Flags.name} <Pascal> [${Flags.root} <dir>] [${Flags.skipGenerate}]`;

/** Runs the generator; returns the report lines. */
export function newModule(argv, env = process.env) {
  const options = parseArguments(argv, [Flags.name]);
  if (options.help) return [USAGE];
  if (!options.name) throw new UsageError(`${Flags.name} is required.`);
  const changes = new ChangeSet(options.root);
  planModule(changes, { name: options.name, existingModules: existingModules(options.root), removedModules: removedModules(options.root) });
  const lines = changes.commit();
  if (options['skip-generate']) return lines;
  return [...lines, ...regenerate(options.root, { apiClient: false, env })];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = runCli(USAGE, () => newModule(process.argv.slice(2)));
}
