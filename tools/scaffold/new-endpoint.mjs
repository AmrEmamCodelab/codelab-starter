#!/usr/bin/env node
// Scaffolds one endpoint in an existing module: a thin controller with [HasPermission], PublicId ids, the
// Application use case with its port and refusal path (problem+json), a validator for writes, a placeholder store,
// unit tests, the error code, the permission when missing, the DI registrations, the MODULE.md note and the
// add-endpoint recipe in the module's rules. Then reruns the rules compile, DocsGen and the frontend API client
// (api:gen). Idempotent for identical arguments; --dry-run prints the files it would write and writes nothing.
//
// Usage: node tools/scaffold/new-endpoint.mjs --module <Name> --name <Pascal> --method <GET|POST|PUT|DELETE>
//        --route <segment> [--permission <kebab action>] [--root <dir>] [--dry-run] [--skip-generate]
// --route: kebab segments ('widgets', 'widgets/archive'; the record id goes last for GET, PUT and DELETE), '.' for the
// module root itself, or a route that places the id once ('{id}/draft', 'widgets/{id}/archive'; not for POST).
// Exit codes: 0 done (or already present), 1 refused (nothing changed), 2 usage or a failed downstream generator.
import { pathToFileURL } from 'node:url';
import { Flags, Report } from './lib/constants.mjs';
import { UsageError, parseArguments, runCli } from './lib/cli.mjs';
import { planEndpoint } from './lib/scaffold.mjs';
import { ChangeSet, DRY_RUN_NOTE, downstreamCommands, existingModules, regenerate } from './lib/workspace.mjs';

const USAGE = `Usage: node tools/scaffold/new-endpoint.mjs ${Flags.module} <Name> ${Flags.name} <Pascal> ${Flags.method} <GET|POST|PUT|DELETE> ${Flags.route} <segment|.|{id}/<segment>> [${Flags.permission} <kebab>] [${Flags.root} <dir>] [${Flags.dryRun}] [${Flags.skipGenerate}]`;
const ROUTE_OPTION = 'route';
const REQUIRED = Object.freeze(['module', 'name', 'method', ROUTE_OPTION]);
/** An endpoint changes the OpenAPI document, so the TypeScript client is regenerated too. */
const GENERATED = Object.freeze({ apiClient: true });

/** Runs the generator; returns the report lines. */
export function newEndpoint(argv, env = process.env) {
  const options = parseArguments(argv, [Flags.module, Flags.name, Flags.method, Flags.route, Flags.permission]);
  if (options.help) return [USAGE];
  const missing = REQUIRED.filter((flag) => options[flag] === undefined || (flag !== ROUTE_OPTION && !options[flag]));
  if (missing.length > 0) throw new UsageError(`Missing: ${missing.map((flag) => `--${flag}`).join(', ')}.`);
  const changes = new ChangeSet(options.root);
  const result = planEndpoint(changes, {
    module: options.module,
    name: options.name,
    method: options.method,
    route: options.route,
    permission: options.permission,
    existingModules: existingModules(options.root),
  });
  const summary = `endpoint ${options.module}.${options.name}: permission ${result.permission.key}, refusal ${result.errorCode}`;
  const notes = result.notes.map((note) => `${Report.note} ${note}`);
  const next = () => downstreamCommands({ ...GENERATED, env }).map((command) => `${Report.next} ${command}`);
  if (options['dry-run']) return [...changes.preview(), ...notes, `${Report.note} ${DRY_RUN_NOTE}`, ...next()];
  const written = changes.commit();
  if (written.length === 0) return [`${Report.unchanged} ${summary}`];
  const lines = [...written, `${Report.created} ${summary}`, ...notes];
  if (options['skip-generate']) return [...lines, ...next()];
  return [...lines, ...regenerate(options.root, { ...GENERATED, env })];
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = runCli(USAGE, () => newEndpoint(process.argv.slice(2)));
}
