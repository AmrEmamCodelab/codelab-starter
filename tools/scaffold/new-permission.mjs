#!/usr/bin/env node
// Adds a permission key to both catalogs: the C# constant in the module's <Module>PermissionKeys and its description
// in <Module>PermissionCatalogSource (plus the MODULE.md note and the add-permission recipe in the module's rules),
// then regenerates docs/generated/permissions.* (Starter.DocsGen) and the frontend PermissionKeys (api:gen), after the
// rules compile. With --no-gen it writes the C# side only and prints the exact two commands that regenerate the rest;
// --dry-run prints the files it would write and writes nothing. Idempotent.
//
// Usage: node tools/scaffold/new-permission.mjs --module <Name> --action <kebab> [--description <text>]
//        [--root <dir>] [--dry-run] [--no-gen] [--skip-generate]
// Exit codes: 0 done (or already present), 1 refused (nothing changed), 2 usage or a failed downstream generator.
import { pathToFileURL } from 'node:url';
import { Flags, Report } from './lib/constants.mjs';
import { UsageError, parseArguments, runCli } from './lib/cli.mjs';
import { frontendPermissionKeyName } from './lib/names.mjs';
import { planPermission } from './lib/scaffold.mjs';
import { ChangeSet, DRY_RUN_NOTE, catalogCommands, downstreamCommands, existingModules, regenerate } from './lib/workspace.mjs';

const USAGE = `Usage: node tools/scaffold/new-permission.mjs ${Flags.module} <Name> ${Flags.action} <kebab> [${Flags.description} <text>] [${Flags.root} <dir>] [${Flags.dryRun}] [${Flags.noGenerate}] [${Flags.skipGenerate}]`;
/** A permission key reaches the frontend PermissionKeys through api:gen. */
const GENERATED = Object.freeze({ apiClient: true });

function nextLines(commands) {
  return commands.map((command) => `${Report.next} ${command}`);
}

/** Runs the generator; returns the report lines. */
export function newPermission(argv, env = process.env) {
  const options = parseArguments(argv, [Flags.module, Flags.action, Flags.description]);
  if (options.help) return [USAGE];
  if (!options.module || !options.action) throw new UsageError(`${Flags.module} and ${Flags.action} are required.`);
  const changes = new ChangeSet(options.root);
  const { constName, key, notes } = planPermission(changes, {
    module: options.module,
    action: options.action,
    description: options.description,
    existingModules: existingModules(options.root),
  });
  const noteLines = notes.map((note) => `${Report.note} ${note}`);
  const frontendKey = `${Report.frontendKey} PermissionKeys.${frontendPermissionKeyName(key)} ("${key}")`;
  if (options['dry-run']) {
    return [...changes.preview(), ...noteLines, frontendKey, `${Report.note} ${DRY_RUN_NOTE}`, ...nextLines(downstreamCommands({ ...GENERATED, env }))];
  }
  const written = changes.commit();
  const lines = written.length > 0 ? [...written, ...noteLines] : [`${Report.unchanged} ${options.module}PermissionKeys.${constName} already declares "${key}"`];
  if (options['no-gen']) return [...lines, frontendKey, ...nextLines(catalogCommands(env))];
  if (written.length === 0) return [...lines, frontendKey];
  if (options['skip-generate']) return [...lines, frontendKey, ...nextLines(downstreamCommands({ ...GENERATED, env }))];
  return [...lines, ...regenerate(options.root, { ...GENERATED, env }), frontendKey];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = runCli(USAGE, () => newPermission(process.argv.slice(2)));
}
