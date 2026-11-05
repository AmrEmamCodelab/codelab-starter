// The run-local orchestration: start (secrets, LocalDB, Seq, API, web, health checks, summary) and stop (only the
// PIDs this tool recorded). Every side effect goes through the `system` object (lib/system.mjs).
import path from 'node:path';
import {
  API_ASSEMBLY,
  API_BUILD_OUTPUT_DIR,
  API_HEALTH_PATH,
  API_PROJECT_DIR,
  API_PROJECT_FILE,
  CMD,
  ExitCode,
  FRONTEND_MANIFEST,
  FRONTEND_START_SCRIPT,
  FRONTEND_WORKSPACE,
  HTTP_OK,
  LOCAL_SECRETS_FILE,
  LOCALDB_INSTANCE,
  LOG_FILES,
  LOG_TAIL_CHARACTERS,
  NODE_MODULES_DIR,
  NPM_SHIM,
  POWERSHELL,
  POWERSHELL_FILE_ARGUMENTS,
  SECRETS_SCRIPT,
  SEQ_SERVICE,
  SERVICE_CONTROL,
  Services,
  SQL_LOCALDB,
  STATE_FILE,
  TASKKILL,
  TASKLIST,
  Timeouts,
  Urls,
  WEB_HEALTH_PATH,
} from './constants.mjs';
import {
  apiAssemblyPath,
  apiEnvironment,
  breakGlassUser,
  parseState,
  pollUntilReady,
  resolveDotnet,
  resolveNodeDir,
  seqDecision,
  seqUrl,
  serializeState,
  taskMatches,
  webDecision,
  webEnvironment,
} from './plan.mjs';

/** A failed start; the message is printed and the exit code is 1. */
export class RunLocalError extends Error {}

function statePath(system) {
  return path.join(system.stateDir, STATE_FILE);
}

function isAlive(system, entry) {
  const result = system.runSync(TASKLIST, ['/FI', `PID eq ${entry.pid}`, '/FO', 'CSV', '/NH']);
  return result.code === 0 && taskMatches(result.output, entry.image);
}

function readState(system) {
  const file = statePath(system);
  return system.exists(file) ? parseState(system.readText(file)) : null;
}

/** Stops every recorded process that is still the one run-local started; returns the report lines. */
export function stop(system) {
  const state = readState(system);
  if (!state) return ['Nothing to stop: run-local has no recorded processes.'];
  const lines = state.processes.map((entry) => {
    if (!isAlive(system, entry)) return `${entry.name} (PID ${entry.pid}) is not running.`;
    const result = system.runSync(TASKKILL, ['/PID', String(entry.pid), '/T', '/F']);
    return result.code === 0 ? `Stopped ${entry.name} (PID ${entry.pid}).` : `Could not stop ${entry.name} (PID ${entry.pid}): ${result.output.trim()}`;
  });
  system.removeStateFile(statePath(system));
  return lines;
}

function ensureSecrets(system, root) {
  if (system.exists(path.join(root, LOCAL_SECRETS_FILE))) return;
  system.log(`Writing the local secrets (${LOCAL_SECRETS_FILE}) with ${SECRETS_SCRIPT}.`);
  const result = system.runSync(POWERSHELL, [...POWERSHELL_FILE_ARGUMENTS, path.join(root, SECRETS_SCRIPT)], { cwd: root });
  if (result.code !== 0) throw new RunLocalError(`${SECRETS_SCRIPT} failed:\n${result.output}`);
}

function startLocalDb(system) {
  const result = system.runSync(SQL_LOCALDB, ['start', LOCALDB_INSTANCE]);
  if (result.code === 0) system.log(`LocalDB ${LOCALDB_INSTANCE} is running.`);
  else system.warn(`could not start LocalDB ${LOCALDB_INSTANCE} (${result.output.trim()}); the API needs it for its database.`);
}

async function prepareSeq(system, noSeq) {
  const url = seqUrl(system.env);
  const reachable = !noSeq && (await system.httpStatus(url)) !== null;
  const serviceInstalled = !noSeq && !reachable && system.runSync(SERVICE_CONTROL, ['query', SEQ_SERVICE]).code === 0;
  const decision = seqDecision({ noSeq, url, reachable, serviceInstalled });
  if (decision.action === 'use') {
    system.log(`Seq is reachable at ${url}.`);
    return url;
  }
  if (decision.action === 'start-service') {
    const started = system.runSync(SERVICE_CONTROL, ['start', SEQ_SERVICE]).code === 0;
    if (started) {
      system.log(`Started the ${SEQ_SERVICE} service (${url}).`);
      return url;
    }
    system.warn(`the ${SEQ_SERVICE} service is installed but did not start (it may need an elevated shell); continuing without Seq.`);
    return null;
  }
  system.warn(decision.reason);
  return null;
}

function logTail(system, logFile) {
  if (!system.exists(logFile)) return '';
  const text = system.readText(logFile);
  return text.slice(-LOG_TAIL_CHARACTERS);
}

async function waitHealthy(system, { name, url, handle, timeoutMs, logFile }) {
  const result = await pollUntilReady({
    probe: async () => (await system.httpStatus(url)) === HTTP_OK,
    exited: handle.exited,
    now: system.now,
    wait: system.wait,
    timeoutMs,
    intervalMs: Timeouts.pollInterval,
  });
  if (result === 'ready') return;
  const why = result === 'exited' ? 'exited before it was healthy' : `was not healthy within ${timeoutMs / 1000} s`;
  throw new RunLocalError(`${name} ${why} (${url}). Last log lines (${logFile}):\n${logTail(system, logFile)}`);
}

/** Builds the API synchronously (a build error is shown here, not hidden in a background log). */
function buildApi(system, root, dotnet) {
  system.log(`Building the API (${API_PROJECT_FILE}).`);
  const result = system.runSync(dotnet, ['build', API_PROJECT_FILE, '-nologo', '-v:q'], { cwd: root });
  if (result.code !== 0) throw new RunLocalError(`The API build failed:\n${result.output.slice(-LOG_TAIL_CHARACTERS)}`);
  const outputDir = path.join(root, API_BUILD_OUTPUT_DIR);
  const assembly = apiAssemblyPath(system.listDirs(outputDir), outputDir, API_ASSEMBLY, system.exists);
  if (!assembly) throw new RunLocalError(`The API built, but ${API_ASSEMBLY} was not found under ${API_BUILD_OUTPUT_DIR}.`);
  return assembly;
}

function startApi(system, root, chosenSeqUrl) {
  const logFile = path.join(system.stateDir, LOG_FILES.api);
  const dotnet = resolveDotnet(system.env, system.exists);
  const assembly = buildApi(system, root, dotnet);
  const handle = system.spawnDetached(dotnet, [assembly], {
    cwd: path.join(root, API_PROJECT_DIR),
    env: apiEnvironment(system.env, chosenSeqUrl),
    logFile,
  });
  return { entry: { name: Services.api.name, pid: handle.pid, image: Services.api.image }, handle, logFile };
}

function webStartDecision(system, root, noWeb) {
  const manifestPath = path.join(root, FRONTEND_MANIFEST);
  const manifest = system.exists(manifestPath) ? JSON.parse(system.readText(manifestPath)) : null;
  const hasNodeModules = system.exists(path.join(root, NODE_MODULES_DIR)) || system.exists(path.join(root, FRONTEND_WORKSPACE, NODE_MODULES_DIR));
  return webDecision({ noWeb, manifest, hasNodeModules, startScript: FRONTEND_START_SCRIPT });
}

function startWeb(system, root) {
  const logFile = path.join(system.stateDir, LOG_FILES.web);
  const nodeDir = resolveNodeDir(system.env, system.exists);
  if (!nodeDir) system.warn('the private Node folder was not found; the web app starts with the Node on PATH.');
  const command = `${NPM_SHIM} ${FRONTEND_START_SCRIPT} --workspace ${FRONTEND_WORKSPACE}`;
  const handle = system.spawnDetached(CMD, ['/d', '/s', '/c', command], { cwd: root, env: webEnvironment(system.env, nodeDir), logFile });
  return { entry: { name: Services.web.name, pid: handle.pid, image: Services.web.image }, handle, logFile };
}

function record(system, processes) {
  system.writeText(statePath(system), serializeState(processes, new Date(system.now()).toISOString()));
}

async function refuseBusyPorts(system, withWeb) {
  const busy = [];
  if ((await system.httpStatus(Urls.api)) !== null) busy.push(Urls.api);
  if (withWeb && (await system.httpStatus(Urls.web)) !== null) busy.push(Urls.web);
  if (busy.length > 0) throw new RunLocalError(`Something already answers on ${busy.join(' and ')}; stop it first (run-local.ps1 -Stop stops only what run-local started).`);
}

/**
 * Starts the local stack and returns the summary lines. On a failed start it stops what it started, then throws.
 * @param {ReturnType<import('./system.mjs').createSystem>} system
 * @param {{ root: string, noWeb: boolean, noSeq: boolean }} options
 */
export async function start(system, { root, noWeb, noSeq }) {
  const running = readState(system)?.processes.filter((entry) => isAlive(system, entry)) ?? [];
  if (running.length > 0) {
    throw new RunLocalError(`run-local is already running (${running.map((entry) => `${entry.name} PID ${entry.pid}`).join(', ')}); run run-local.ps1 -Stop first.`);
  }
  const web = webStartDecision(system, root, noWeb);
  await refuseBusyPorts(system, web.start);
  ensureSecrets(system, root);
  startLocalDb(system);
  const chosenSeq = await prepareSeq(system, noSeq);

  const started = [];
  try {
    const api = startApi(system, root, chosenSeq);
    started.push(api.entry);
    record(system, started);
    let webStarted = null;
    if (web.start) {
      webStarted = startWeb(system, root);
      started.push(webStarted.entry);
      record(system, started);
    } else {
      system.warn(`web not started: ${web.reason}`);
    }
    system.log(`Waiting for the API (${Urls.api}${API_HEALTH_PATH}).`);
    await waitHealthy(system, { name: 'The API', url: `${Urls.api}${API_HEALTH_PATH}`, handle: api.handle, timeoutMs: Timeouts.api, logFile: api.logFile });
    if (webStarted) {
      system.log(`Waiting for the web app (${Urls.web}${WEB_HEALTH_PATH}).`);
      await waitHealthy(system, { name: 'The web app', url: `${Urls.web}${WEB_HEALTH_PATH}`, handle: webStarted.handle, timeoutMs: Timeouts.web, logFile: webStarted.logFile });
    }
    return summary(system, root, { web: Boolean(webStarted), seq: chosenSeq });
  } catch (error) {
    if (started.length > 0) for (const line of stop(system)) system.log(line);
    throw error;
  }
}

function summary(system, root, { web, seq }) {
  const secrets = path.join(root, LOCAL_SECRETS_FILE);
  const user = system.exists(secrets) ? breakGlassUser(system.readText(secrets)) : null;
  return [
    'The local stack is up.',
    `  API      ${Urls.api}  (health ${Urls.api}${API_HEALTH_PATH}, log ${path.join(system.stateDir, LOG_FILES.api)})`,
    web ? `  Web      ${Urls.web}  (log ${path.join(system.stateDir, LOG_FILES.web)})` : '  Web      not started',
    seq ? `  Seq      ${seq}` : '  Seq      not used',
    `  Sign in  ${user ?? '(unknown)'}, password in ${LOCAL_SECRETS_FILE} (Identity:BreakGlass:Password)`,
    'Stop with: powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Stop',
  ];
}

/** The CLI entry: returns the exit code. */
export async function main(options, system) {
  try {
    const lines = options.stop ? stop(system) : await start(system, options);
    for (const line of lines) system.log(line);
    return ExitCode.ok;
  } catch (error) {
    if (!(error instanceof RunLocalError)) throw error;
    system.warn(error.message);
    return ExitCode.failed;
  }
}
