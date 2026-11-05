// The real side effects of run-local, behind one object so the orchestration can be tested with a fake: detached
// starts with logs, synchronous helper commands, HTTP probes, the clock and the state file.
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { STATE_DIR_NAME, Timeouts, UTF8 } from './constants.mjs';

const OUTPUT_LIMIT_BYTES = 16 * 1024 * 1024;
const APPEND = 'a';

/** The system run-local drives. */
export function createSystem() {
  return {
    env: process.env,
    stateDir: path.join(os.tmpdir(), STATE_DIR_NAME),
    exists: (file) => fs.existsSync(file),
    /** The sub-folder names of `dir` ([] when it does not exist). */
    listDirs: (dir) => (fs.existsSync(dir) ? fs.readdirSync(dir, { withFileTypes: true }).filter((entry) => entry.isDirectory()).map((entry) => entry.name) : []),
    readText: (file) => fs.readFileSync(file, UTF8),
    writeText: (file, text) => {
      fs.mkdirSync(path.dirname(file), { recursive: true });
      fs.writeFileSync(file, text, UTF8);
    },
    /** Removes the state file run-local wrote; never anything else. */
    removeStateFile: (file) => fs.rmSync(file, { force: true }),
    /** Runs a helper command to completion: { code, output } (code null when it could not start). */
    runSync: (command, args, options = {}) => {
      const result = spawnSync(command, args, { encoding: UTF8, maxBuffer: OUTPUT_LIMIT_BYTES, windowsHide: true, ...options });
      if (result.error) return { code: null, output: result.error.message };
      return { code: result.status, output: `${result.stdout ?? ''}${result.stderr ?? ''}` };
    },
    /** Starts a long-running process detached, its output appended to `logFile`; returns { pid, exited() }. */
    spawnDetached: (command, args, { cwd, env, logFile }) => {
      fs.mkdirSync(path.dirname(logFile), { recursive: true });
      const log = fs.openSync(logFile, APPEND);
      const child = spawn(command, args, { cwd, env, detached: true, stdio: ['ignore', log, log], windowsHide: true });
      let done = false;
      child.on('exit', () => { done = true; });
      child.on('error', () => { done = true; });
      child.unref();
      fs.closeSync(log);
      return { pid: child.pid, exited: () => done };
    },
    /** The HTTP status of GET url, or null when nothing answers. */
    httpStatus: async (url) => {
      try {
        const response = await fetch(url, { signal: AbortSignal.timeout(Timeouts.probe), redirect: 'manual' });
        return response.status;
      } catch {
        return null;
      }
    },
    now: () => Date.now(),
    wait: (ms) => new Promise((resolve) => { setTimeout(resolve, ms); }),
    log: (line) => console.log(line),
    warn: (line) => console.warn(`warning: ${line}`),
  };
}
