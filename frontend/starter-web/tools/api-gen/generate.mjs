#!/usr/bin/env node
// Generates the typed API client and the permission keys from the backend's generated docs (contract section 8).
//
//   npm run api:gen          node tools/api-gen/generate.mjs           write the outputs
//   npm run api:gen:check    node tools/api-gen/generate.mjs --check   exit 1 when an output is stale
//
// Inputs (relative to frontend/starter-web, overridable with --openapi / --permissions):
//   ../../docs/generated/openapi.v1.json, ../../docs/generated/permissions.json
// Outputs:
//   src/app/core/api/generated/openapi.ts, src/app/core/permissions/permission-keys.generated.ts
// Exit codes: 0 ok, 1 drift or failure, 2 usage.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { ApiGenPaths, CliFlags, ExitCodes, REGENERATE_COMMAND } from './lib/constants.mjs';
import { loadGeneratorModules, renderApiClient } from './lib/openapi-client.mjs';
import { syncOutputs } from './lib/outputs.mjs';
import { renderPermissionKeys } from './lib/permission-keys.mjs';

/** The app folder (frontend/starter-web): two levels above this script. */
export const APP_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

const USAGE = `Usage: node tools/api-gen/generate.mjs [${CliFlags.check}] [${CliFlags.openapi} <file>] [${CliFlags.permissions} <file>]`;

/** Parses argv into `{ check, openapi, permissions, help }` or `{ error }`. Paths stay as given. */
export function parseArguments(argv) {
  const parsed = { check: false, openapi: ApiGenPaths.openapiInput, permissions: ApiGenPaths.permissionsInput, help: false };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === CliFlags.check) parsed.check = true;
    else if (argument === CliFlags.help) parsed.help = true;
    else if (argument === CliFlags.openapi || argument === CliFlags.permissions) {
      const value = argv[index + 1];
      if (value === undefined || value.startsWith('--')) return { error: `${argument} needs a file path.` };
      parsed[argument === CliFlags.openapi ? 'openapi' : 'permissions'] = value;
      index += 1;
    } else {
      return { error: `Unknown argument: ${argument}` };
    }
  }
  return parsed;
}

function readJson(filePath, label) {
  if (!fs.existsSync(filePath)) {
    throw new Error(`${label} not found at ${filePath}. Run Starter.DocsGen first (dotnet run --project backend/tools/Starter.DocsGen -- --out docs/generated).`);
  }
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch (error) {
    throw new Error(`${label} at ${filePath} is not valid JSON: ${error.message}`, { cause: error });
  }
}

/**
 * Builds every output in memory. `modules` is `{ openapi, ts }`; it is loaded when not given.
 * Returns `[{ path, content }]` with absolute paths under `appDir`.
 */
export async function buildOutputs({ appDir = APP_DIR, openapiPath, permissionsPath, modules }) {
  const openapiDocument = readJson(path.resolve(appDir, openapiPath), 'The OpenAPI document');
  const permissionsDocument = readJson(path.resolve(appDir, permissionsPath), 'permissions.json');
  const generatorModules = modules ?? (await loadGeneratorModules());
  return [
    { path: path.resolve(appDir, ApiGenPaths.clientOutput), content: await renderApiClient(openapiDocument, generatorModules) },
    { path: path.resolve(appDir, ApiGenPaths.permissionKeysOutput), content: renderPermissionKeys(permissionsDocument) },
  ];
}

/** Runs the generator; returns the exit code. `log`/`error` default to the console. */
export async function run(argv, { appDir = APP_DIR, modules, log = console.log, error = console.error } = {}) {
  const options = parseArguments(argv);
  if (options.error) {
    error(`${options.error}\n${USAGE}`);
    return ExitCodes.usage;
  }
  if (options.help) {
    log(USAGE);
    return ExitCodes.ok;
  }
  try {
    const outputs = await buildOutputs({ appDir, openapiPath: options.openapi, permissionsPath: options.permissions, modules });
    const { drifted, written } = syncOutputs(outputs, { check: options.check });
    const relative = (filePath) => path.relative(appDir, filePath).replaceAll('\\', '/');
    if (options.check) {
      if (drifted.length === 0) return ExitCodes.ok;
      error(`Generated API files are stale; run \`${REGENERATE_COMMAND}\`:\n${drifted.map((file) => `  ${relative(file)}`).join('\n')}`);
      return ExitCodes.failure;
    }
    log(written.length === 0 ? 'api-gen: outputs are up to date.' : `api-gen: wrote\n${written.map((file) => `  ${relative(file)}`).join('\n')}`);
    return ExitCodes.ok;
  } catch (failure) {
    error(`api-gen: ${failure.message}`);
    return ExitCodes.failure;
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.exitCode = await run(process.argv.slice(2));
}
