// Runs the fork tools' PowerShell entry points the way a person does (Windows PowerShell 5.1), plus temp folders.
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';

const POWERSHELL = 'powershell.exe';
const BASE_ARGS = ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass'];
const TEMP_PREFIX = 'codelab-fork-test-';
const MAX_OUTPUT_BYTES = 64 * 1024 * 1024;
const NODE_DIR_ENV = 'CODELAB_NODE_DIR';

/** Test identity for every git commit the tools make in scratch repos. */
export const GIT_IDENTITY = Object.freeze({
  GIT_AUTHOR_NAME: 'Fork Test',
  GIT_AUTHOR_EMAIL: 'fork-test@example.test',
  GIT_COMMITTER_NAME: 'Fork Test',
  GIT_COMMITTER_EMAIL: 'fork-test@example.test',
});

/** The environment for child processes: the git identity and this node first (the scripts use CODELAB_NODE_DIR). */
export function testEnvironment(extra = {}) {
  return { ...process.env, ...GIT_IDENTITY, [NODE_DIR_ENV]: dirname(process.execPath), ...extra };
}

/** Runs a .ps1 file with arguments; returns { code, stdout, stderr, output }. */
export function runScript(scriptPath, args, options = {}) {
  const result = spawnSync(POWERSHELL, [...BASE_ARGS, '-File', scriptPath, ...args], {
    encoding: 'utf8',
    maxBuffer: MAX_OUTPUT_BYTES,
    cwd: options.cwd ?? dirname(scriptPath),
    env: testEnvironment(options.env),
    windowsHide: true,
  });
  if (result.error) throw result.error;
  return { code: result.status, stdout: result.stdout ?? '', stderr: result.stderr ?? '', output: `${result.stdout}${result.stderr}` };
}

/** A fresh temp folder and its cleanup. */
export function makeTempDir() {
  const path = mkdtempSync(join(tmpdir(), TEMP_PREFIX));
  return { path, cleanup: () => rmSync(path, { recursive: true, force: true }) };
}
