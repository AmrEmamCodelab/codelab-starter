// Child processes of the fork tools: git, node, npm and Windows PowerShell. Every call is synchronous and checked.
import { spawnSync } from 'node:child_process';
import { GIT, MAX_PROCESS_OUTPUT_BYTES, NPM_COMMAND, POWERSHELL, POWERSHELL_ARGUMENTS } from './constants.mjs';

const UTF16_LE = 'utf16le';
const BASE64 = 'base64';
const ENCODED_COMMAND = '-EncodedCommand';
const FILE_SWITCH = '-File';
/** Pinned git settings: no path quoting surprises, no pager, no colour, no external diff. */
const GIT_CONFIG = Object.freeze(['-c', 'core.quotePath=false', '-c', 'color.ui=false', '-c', 'core.pager=cat']);

/** Raised when a checked child process fails. */
export class ProcessError extends Error {
  constructor(message, result) {
    super(message);
    this.result = result;
  }
}

/**
 * Runs a command and returns { code, stdout, stderr }; with `check` a non-zero exit throws ProcessError.
 * @param {string} command
 * @param {string[]} args
 * @param {{ cwd: string, input?: string | Buffer, env?: NodeJS.ProcessEnv, check?: boolean, binary?: boolean, inherit?: boolean, shell?: boolean }} options
 */
export function run(command, args, options) {
  const result = spawnSync(command, args, {
    cwd: options.cwd,
    input: typeof options.input === 'string' ? Buffer.from(options.input, 'utf8') : options.input,
    env: options.env ?? process.env,
    encoding: options.binary ? 'buffer' : 'utf8',
    maxBuffer: MAX_PROCESS_OUTPUT_BYTES,
    stdio: options.inherit ? ['ignore', 'inherit', 'inherit'] : 'pipe',
    shell: options.shell ?? false,
    windowsHide: true,
  });
  if (result.error) throw new ProcessError(`${command} could not start: ${result.error.message}`, result);
  const outcome = { code: result.status, stdout: result.stdout ?? '', stderr: result.stderr ?? '' };
  if (options.check !== false && outcome.code !== 0) {
    throw new ProcessError(`${command} ${args.join(' ')} failed (exit ${outcome.code}) in ${options.cwd}:\n${outcome.stderr}`, outcome);
  }
  return outcome;
}

/** Runs git with the pinned settings. */
export function git(cwd, args, options = {}) {
  return run(GIT, [...GIT_CONFIG, ...args], { cwd, ...options });
}

/** Runs git and returns trimmed stdout. */
export function gitText(cwd, args, options = {}) {
  return git(cwd, args, options).stdout.trim();
}

/** Runs git and returns its NUL-separated output as a list (for -z listings). */
export function gitList(cwd, args) {
  return git(cwd, args).stdout.split('\0').filter((entry) => entry.length > 0);
}

/** Runs a PowerShell script file with arguments (Windows PowerShell 5.1). */
export function powershellFile(cwd, scriptPath, args, options = {}) {
  return run(POWERSHELL, [...POWERSHELL_ARGUMENTS, FILE_SWITCH, scriptPath, ...args], { cwd, ...options });
}

/** Runs free PowerShell text; -EncodedCommand keeps quotes intact whatever the text holds. */
export function powershellCommand(cwd, commandText, options = {}) {
  const encoded = Buffer.from(commandText, UTF16_LE).toString(BASE64);
  return run(POWERSHELL, [...POWERSHELL_ARGUMENTS, ENCODED_COMMAND, encoded], { cwd, ...options });
}

/** Runs node (this same executable) on a script. */
export function node(cwd, scriptPath, args, options = {}) {
  return run(process.execPath, [scriptPath, ...args], { cwd, ...options });
}

/** Runs npm (npm.cmd on Windows needs a shell since Node 20's spawn hardening). */
export function npm(cwd, args, options = {}) {
  return run(NPM_COMMAND, args, { cwd, shell: process.platform === 'win32', ...options });
}
