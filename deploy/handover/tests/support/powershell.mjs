// Runs Windows PowerShell 5.1 (the scripts' baseline) for the handover kit tests.
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
export const HANDOVER_DIR = resolve(HERE, '..', '..');
export const MODULE_PATH = join(HANDOVER_DIR, 'lib', 'HandoverKit.psm1');
export const NEW_KIT_SCRIPT = join(HANDOVER_DIR, 'New-HandoverKit.ps1');
export const TEST_KIT_SCRIPT = join(HANDOVER_DIR, 'Test-HandoverKit.ps1');

export const EXIT = Object.freeze({ success: 0, failure: 1, usage: 2 });

const POWERSHELL = 'powershell.exe';
const BASE_ARGS = ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass'];
const TEMP_PREFIX = 'codelab-handover-unit-';
const MAX_OUTPUT_BYTES = 64 * 1024 * 1024;

/** Quotes a value as a PowerShell single-quoted literal. */
export function psQuote(value) {
  return `'${String(value).replaceAll("'", "''")}'`;
}

function run(args, options = {}) {
  const result = spawnSync(POWERSHELL, [...BASE_ARGS, ...args], {
    encoding: 'utf8',
    maxBuffer: MAX_OUTPUT_BYTES,
    cwd: options.cwd ?? HANDOVER_DIR,
  });
  if (result.error) throw result.error;
  return { code: result.status, stdout: result.stdout ?? '', stderr: result.stderr ?? '', output: `${result.stdout}${result.stderr}` };
}

/** Runs a script file with named arguments (an array of strings, e.g. ['-OutputPath', 'x']). */
export function runScript(scriptPath, args, options) {
  return run(['-File', scriptPath, ...args], options);
}

/** Imports the kit module and runs the given PowerShell text. */
export function runWithModule(body) {
  const command = `$ErrorActionPreference = 'Stop'; Import-Module ${psQuote(MODULE_PATH)} -Force -DisableNameChecking; ${body}`;
  return run(['-Command', command]);
}

/** Runs PowerShell text with the module and parses its stdout as JSON (the text must end in ConvertTo-Json). */
export function jsonWithModule(body) {
  const result = runWithModule(body);
  if (result.code !== EXIT.success) {
    throw new Error(`PowerShell failed (${result.code}):\n${result.output}`);
  }
  return JSON.parse(result.stdout);
}

/** A fresh temp folder removed by the returned cleanup function. */
export function makeTempDir() {
  const path = mkdtempSync(join(tmpdir(), TEMP_PREFIX));
  return { path, cleanup: () => rmSync(path, { recursive: true, force: true }) };
}

/** Writes a file (creating folders) under a root. */
export function writeFile(root, relativePath, content) {
  const full = join(root, relativePath);
  mkdirSync(dirname(full), { recursive: true });
  writeFileSync(full, content);
  return full;
}
