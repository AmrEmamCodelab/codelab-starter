// The run-local orchestration: start (secrets, LocalDB, Seq, the API, the Attachment service, the web app, health
// checks, summary) and stop (only the PIDs this tool recorded). Every side effect goes through the `system` object
// (lib/system.mjs).
import path from 'node:path';
import {
  ATTACHMENTS_PROJECT_FILE,
  ATTACHMENTS_SECRETS_FILE,
  CMD,
  CONNECTION_STRING_ENV,
  ExitCode,
  FRONTEND_MANIFEST,
  FRONTEND_START_SCRIPT,
  FRONTEND_WORKSPACE,
  HTTP_OK,
  Hosts,
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
  hostEnvironment,
  attachmentsDecision,
  breakGlassUser,
  parseState,
  pollUntilReady,
  portsToCheck,
  resolveDotnet,
  resolveNodeDir,
  secretsNeedSeeding,
  seqDecision,
  seqUrl,
  serializeState,
  startPlan,
  taskMatches,
  usesDefaultLocalDb,
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

/** Runs New-LocalSecrets.ps1 when a secrets file or the attachment keys are missing (it only adds missing keys). */
function ensureSecrets(system, root, withAttachments) {
  const apiSecrets = path.join(root, LOCAL_SECRETS_FILE);
  const needed = secretsNeedSeeding({
    apiSecretsText: system.exists(apiSecrets) ? system.readText(apiSecrets) : null,
    serviceSecretsExists: system.exists(path.join(root, ATTACHMENTS_SECRETS_FILE)),
    withAttachments,
  });
  if (!needed) return;
  system.log(`Writing the missing local secrets with ${SECRETS_SCRIPT} (existing values are kept).`);
  const result = system.runSync(POWERSHELL, [...POWERSHELL_FILE_ARGUMENTS, path.join(root, SECRETS_SCRIPT)], { cwd: root });
  if (result.code !== 0) throw new RunLocalError(`${SECRETS_SCRIPT} failed:\n${result.output}`);
}

function startLocalDb(system) {
  if (!usesDefaultLocalDb(system.env)) {
    system.log(`${CONNECTION_STRING_ENV} is set to another server: LocalDB ${LOCALDB_INSTANCE} is not started.`);
    return;
  }
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

async function waitHealthy(system, { label, url, handle, timeoutMs, logFile }) {
  system.log(`Waiting for ${label.toLowerCase()} (${url}).`);
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
  throw new RunLocalError(`${label} ${why} (${url}). Last log lines (${logFile}):\n${logTail(system, logFile)}`);
}

/** Builds a .NET host synchronously (a build error is shown here, not hidden in a background log). */
function buildHost(system, root, dotnet, host) {
  system.log(`Building ${host.label.toLowerCase()} (${host.projectFile}).`);
  const result = system.runSync(dotnet, ['build', host.projectFile, '-nologo', '-v:q'], { cwd: root });
  if (result.code !== 0) throw new RunLocalError(`${host.label} build failed:\n${result.output.slice(-LOG_TAIL_CHARACTERS)}`);
  const outputDir = path.join(root, host.outputDir);
  const assembly = apiAssemblyPath(system.listDirs(outputDir), outputDir, host.assembly, system.exists);
  if (!assembly) throw new RunLocalError(`${host.label} built, but ${host.assembly} was not found under ${host.outputDir}.`);
  return assembly;
}

/**
 * Builds a .NET host, then runs its assembly detached from the project folder in Development on its URL (a detached
 * `dotnet run` exits at once on Windows).
 */
function startHost(system, root, host, chosenSeqUrl) {
  const logFile = path.join(system.stateDir, host.logFile);
  const dotnet = resolveDotnet(system.env, system.exists);
  const assembly = buildHost(system, root, dotnet, host);
  const handle = system.spawnDetached(dotnet, [assembly], {
    cwd: path.join(root, host.projectDir),
    env: hostEnvironment(system.env, host.url, chosenSeqUrl),
    logFile,
  });
  const entry = { name: host.service.name, pid: handle.pid, image: host.service.image };
  return { entry, handle, logFile, label: host.label, url: `${host.url}${host.healthPath}`, timeoutMs: host.timeoutMs };
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
  const entry = { name: Services.web.name, pid: handle.pid, image: Services.web.image };
  return { entry, handle, logFile, label: 'The web app', url: `${Urls.web}${WEB_HEALTH_PATH}`, timeoutMs: Timeouts.web };
}

function record(system, processes) {
  system.writeText(statePath(system), serializeState(processes, new Date(system.now()).toISOString()));
}

async function refuseBusyPorts(system, plan) {
  const busy = [];
  for (const url of portsToCheck(plan)) {
    if ((await system.httpStatus(url)) !== null) busy.push(url);
  }
  if (busy.length > 0) throw new RunLocalError(`Something already answers on ${busy.join(' and ')}; stop it first (run-local.ps1 -Stop stops only what run-local started).`);
}

/** Decides what starts: the API always, the Attachment service and the web app when they can (warning otherwise). */
function decide(system, root, { noWeb, noAttachments }) {
  const web = webStartDecision(system, root, noWeb);
  const attachments = attachmentsDecision({ noAttachments, projectExists: system.exists(path.join(root, ATTACHMENTS_PROJECT_FILE)) });
  if (!attachments.start) system.warn(`Attachment service not started: ${attachments.reason}`);
  if (!web.start) system.warn(`web not started: ${web.reason}`);
  return startPlan({ attachments: attachments.start, web: web.start });
}

/** Starts the planned services in order, recording each PID as soon as it exists; returns the started handles. */
function launch(system, root, plan, chosenSeq, started) {
  const launched = [];
  for (const name of plan) {
    const service = name === Services.web.name ? startWeb(system, root) : startHost(system, root, Hosts[name], chosenSeq);
    launched.push(service);
    started.push(service.entry);
    record(system, started);
  }
  return launched;
}

/**
 * Starts the local stack and returns the summary lines. On a failed start it stops what it started, then throws.
 * @param {ReturnType<import('./system.mjs').createSystem>} system
 * @param {{ root: string, noWeb: boolean, noSeq: boolean, noAttachments?: boolean }} options
 */
export async function start(system, { root, noWeb, noSeq, noAttachments = false }) {
  const running = readState(system)?.processes.filter((entry) => isAlive(system, entry)) ?? [];
  if (running.length > 0) {
    throw new RunLocalError(`run-local is already running (${running.map((entry) => `${entry.name} PID ${entry.pid}`).join(', ')}); run run-local.ps1 -Stop first.`);
  }
  const plan = decide(system, root, { noWeb, noAttachments });
  await refuseBusyPorts(system, plan);
  ensureSecrets(system, root, plan.includes(Services.attachments.name));
  startLocalDb(system);
  const chosenSeq = await prepareSeq(system, noSeq);

  const started = [];
  try {
    const launched = launch(system, root, plan, chosenSeq, started);
    for (const service of launched) await waitHealthy(system, service);
    return summary(system, root, { plan, seq: chosenSeq });
  } catch (error) {
    if (started.length > 0) for (const line of stop(system)) system.log(line);
    throw error;
  }
}

function summary(system, root, { plan, seq }) {
  const secrets = path.join(root, LOCAL_SECRETS_FILE);
  const user = system.exists(secrets) ? breakGlassUser(system.readText(secrets)) : null;
  const log = (name) => path.join(system.stateDir, LOG_FILES[name]);
  const has = (name) => plan.includes(name);
  const attachments = Hosts.attachments;
  return [
    'The local stack is up.',
    `  API      ${Urls.api}  (health ${Urls.api}${Hosts.api.healthPath}, log ${log(Services.api.name)})`,
    has(Services.attachments.name)
      ? `  Files    ${attachments.url}  (Attachment service; health ${attachments.url}${attachments.healthPath}, log ${log(Services.attachments.name)})`
      : '  Files    Attachment service not started',
    has(Services.web.name) ? `  Web      ${Urls.web}  (log ${log(Services.web.name)})` : '  Web      not started',
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
