// File-system and process side of the generators: a change set that is written all at once (a refusal changes
// nothing), the module inventory, and the downstream generators (rules compile, DocsGen, api:gen).
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  API_GEN_SCRIPT,
  DOCS_GENERATED_DIR,
  DOCSGEN_PROJECT,
  DOTNET_ENV,
  DOTNET_ON_PATH,
  FRONTEND_MANIFEST,
  FRONTEND_WORKSPACE,
  MANIFEST_FILE,
  MAX_PROCESS_OUTPUT_BYTES,
  MODULES_ROOT,
  ModuleFiles,
  NODE_MODULES_DIR,
  NPM_COMMAND,
  OPENAPI_DOCUMENT,
  PRIVATE_DOTNET,
  Report,
  RULES_COMPILE_SCRIPT,
  SKIP_GENERATE_ENV,
  UTF8,
} from './constants.mjs';

/** The repo root: two folders above tools/scaffold/lib's parent. */
export const DEFAULT_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

/** Raised for a failed downstream generator. */
export class GeneratorError extends Error {}

/** Pending writes; `commit` writes them and returns the report lines ("created <path>" / "changed <path>"). */
export class ChangeSet {
  constructor(root) {
    this.root = root;
    this.files = new Map();
  }

  /** The current text of a repo-relative file: the pending version, else the disk version, else null. */
  read(relativePath) {
    if (this.files.has(relativePath)) return this.files.get(relativePath);
    const full = path.join(this.root, relativePath);
    return fs.existsSync(full) ? fs.readFileSync(full, UTF8) : null;
  }

  /** Records a write; a write that changes nothing is dropped. */
  write(relativePath, content) {
    if (this.read(relativePath) === content) return;
    this.files.set(relativePath, content);
  }

  /** True when the file exists on disk. */
  exists(relativePath) {
    return fs.existsSync(path.join(this.root, relativePath));
  }

  /** Writes every pending file. */
  commit() {
    const lines = [];
    for (const [relativePath, content] of this.files) {
      const full = path.join(this.root, relativePath);
      const kind = fs.existsSync(full) ? Report.changed : Report.created;
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, content, UTF8);
      lines.push(`${kind} ${relativePath}`);
    }
    return lines;
  }
}

/** The modules present: folders under backend/src/Modules holding their Starter.Modules.<Name>.csproj. */
export function existingModules(root) {
  const dir = path.join(root, MODULES_ROOT);
  if (!fs.existsSync(dir)) return [];
  return fs.readdirSync(dir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && fs.existsSync(path.join(dir, entry.name, ModuleFiles.project(entry.name))))
    .map((entry) => entry.name)
    .sort();
}

/** codelab.starter.json removedModules ([] when the manifest is absent). */
export function removedModules(root) {
  const file = path.join(root, MANIFEST_FILE);
  if (!fs.existsSync(file)) return [];
  const manifest = JSON.parse(fs.readFileSync(file, UTF8));
  return Array.isArray(manifest.removedModules) ? manifest.removedModules : [];
}

/** The dotnet executable: CODELAB_DOTNET, then the private SDK, then dotnet on PATH. */
export function resolveDotnet(env = process.env, exists = fs.existsSync) {
  if (env[DOTNET_ENV]) return env[DOTNET_ENV];
  return exists(PRIVATE_DOTNET) ? PRIVATE_DOTNET : DOTNET_ON_PATH;
}

/** Runs a command and throws GeneratorError on failure. A .cmd shim runs through the shell as one command line. */
function run(command, args, cwd) {
  const viaShell = command.endsWith('.cmd');
  const options = { cwd, encoding: UTF8, maxBuffer: MAX_PROCESS_OUTPUT_BYTES, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true };
  const result = viaShell
    ? spawnSync([command, ...args].join(' '), { ...options, shell: true })
    : spawnSync(command, args, options);
  if (result.error) throw new GeneratorError(`${command} could not start: ${result.error.message}`);
  if (result.status !== 0) {
    throw new GeneratorError(`${command} ${args.join(' ')} failed (exit ${result.status}):\n${result.stdout}\n${result.stderr}`);
  }
}

function frontendHasScript(root, script) {
  const manifest = path.join(root, FRONTEND_MANIFEST);
  if (!fs.existsSync(manifest)) return false;
  return Boolean(JSON.parse(fs.readFileSync(manifest, UTF8)).scripts?.[script]);
}

/**
 * The two commands that regenerate both permission catalogs after a key change: Starter.DocsGen (the C# catalog to
 * docs/generated/permissions.*) and api:gen (permissions.json to the frontend PermissionKeys). Printed by
 * new-permission --no-gen; `regenerate` runs the same two.
 */
export function catalogCommands(env = process.env) {
  return [
    `${resolveDotnet(env)} run --project ${DOCSGEN_PROJECT} -- --out ${DOCS_GENERATED_DIR}`,
    `npm run ${API_GEN_SCRIPT} --workspace ${FRONTEND_WORKSPACE}`,
  ];
}

/**
 * Runs the downstream generators that exist: the rules compile (always, when its dependencies are installed),
 * DocsGen (when its project exists), then api:gen (with `apiClient`, when the frontend and the OpenAPI document exist).
 * Returns the report lines ("ran ..." / "skipped ...: reason").
 */
export function regenerate(root, { apiClient, docs = true, env = process.env, log = () => {} }) {
  if (env[SKIP_GENERATE_ENV]) return [`${Report.skipped} downstream generators: ${SKIP_GENERATE_ENV} is set`];
  const lines = [];
  const step = (line) => { lines.push(line); log(line); };
  if (fs.existsSync(path.join(root, NODE_MODULES_DIR))) {
    run(process.execPath, [RULES_COMPILE_SCRIPT], root);
    step(`${Report.ran} node ${RULES_COMPILE_SCRIPT}`);
  } else {
    step(`${Report.skipped} node ${RULES_COMPILE_SCRIPT}: ${NODE_MODULES_DIR} is missing; run npm install, then the compile`);
  }
  if (docs && fs.existsSync(path.join(root, DOCSGEN_PROJECT))) {
    run(resolveDotnet(env), ['run', '--project', DOCSGEN_PROJECT, '--', '--out', DOCS_GENERATED_DIR], root);
    step(`${Report.ran} Starter.DocsGen --out ${DOCS_GENERATED_DIR}`);
  } else if (docs) {
    step(`${Report.skipped} Starter.DocsGen: ${DOCSGEN_PROJECT} does not exist yet`);
  }
  if (!apiClient) return lines;
  const ready = frontendHasScript(root, API_GEN_SCRIPT) && fs.existsSync(path.join(root, OPENAPI_DOCUMENT))
    && fs.existsSync(path.join(root, NODE_MODULES_DIR));
  if (ready) {
    run(NPM_COMMAND, ['run', API_GEN_SCRIPT, '--workspace', FRONTEND_WORKSPACE], root);
    step(`${Report.ran} npm run ${API_GEN_SCRIPT} --workspace ${FRONTEND_WORKSPACE}`);
  } else {
    step(`${Report.skipped} npm run ${API_GEN_SCRIPT}: the frontend script, ${OPENAPI_DOCUMENT} or ${NODE_MODULES_DIR} is missing`);
  }
  return lines;
}
