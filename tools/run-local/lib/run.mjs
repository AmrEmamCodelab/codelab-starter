// The run-local orchestration: start (secrets, LocalDB, Seq, the API, the Attachment service, the web app, health
// checks, summary) and stop (only the PIDs this tool recorded). Every side effect goes through the `system` object
// (lib/system.mjs).
import path from 'node:path';
import {
  ANGULAR_BUILD_ARGUMENTS,
  ANGULAR_CLI,
  API_DEVELOPMENT_SETTINGS,
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
  ICONS_SUBSET_ARGUMENTS,
  ICONS_SUBSET_SCRIPT,
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
  SLOT_BUILD_DIR,
  SLOT_STORAGE_DIR,
  SLOT_WEB_DIR,
  STATIC_SERVER_SCRIPT,
  Services,
  Slots,
  SQL_LOCALDB,
  STATE_FILE,
  TASKKILL,
  TASKLIST,
  Timeouts,
  WEB_BROWSER_DIR,
  WEB_HEALTH_PATH,
  WEB_RUNTIME_CONFIG,
} from './constants.mjs';
import {
  apiAssemblyPath,
  hostEnvironment,
  attachmentsDecision,
  breakGlassUser,
  developmentConnectionString,
  parseState,
  pollUntilReady,
  portsToCheck,
  resolveDotnet,
  resolveNodeDir,
  secretsNeedSeeding,
  seqDecision,
  seqUrl,
  serializeState,
  slotOverrides,
  slotStateDir,
  slotUrls,
  slotWebConfig,
  startPlan,
  taskMatches,
  usesDefaultLocalDb,
  webDecision,
  webEnvironment,
} from './plan.mjs';
import { ServerFlags } from './static.mjs';

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

/** The system as a slot sees it: slot n keeps its state file, logs and build output in its own folder. */
function forSlot(system, slot) {
  return slot === Slots.default ? system : { ...system, stateDir: slotStateDir(system.stateDir, slot) };
}

/** The run-local.ps1 argument that names a slot ('' for the default slot). */
function slotArgument(slot) {
  return slot === Slots.default ? '' : ` -Slot ${slot}`;
}

/** Stops every recorded process of the slot that is still the one run-local started; returns the report lines. */
export function stop(baseSystem, slot = Slots.default) {
  const system = forSlot(baseSystem, slot);
  const state = readState(system);
  if (!state) return [`Nothing to stop: run-local has no recorded processes${slot === Slots.default ? '' : ` in slot ${slot}`}.`];
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

/**
 * Builds a .NET host synchronously (a build error is shown here, not hidden in a background log). A slot builds into
 * its own folder, so the assemblies a running default stack holds open are never overwritten.
 */
function buildHost(system, root, dotnet, host, slot) {
  system.log(`Building ${host.label.toLowerCase()} (${host.projectFile}).`);
  const slotOutput = slot === Slots.default ? null : path.join(system.stateDir, SLOT_BUILD_DIR, host.service.name);
  const output = slotOutput ? ['-o', slotOutput] : [];
  const result = system.runSync(dotnet, ['build', host.projectFile, '-nologo', '-v:q', ...output], { cwd: root });
  if (result.code !== 0) throw new RunLocalError(`${host.label} build failed:\n${result.output.slice(-LOG_TAIL_CHARACTERS)}`);
  if (slotOutput) {
    const built = path.join(slotOutput, host.assembly);
    if (!system.exists(built)) throw new RunLocalError(`${host.label} built, but ${host.assembly} was not found under ${slotOutput}.`);
    return built;
  }
  const outputDir = path.join(root, host.outputDir);
  const assembly = apiAssemblyPath(system.listDirs(outputDir), outputDir, host.assembly, system.exists);
  if (!assembly) throw new RunLocalError(`${host.label} built, but ${host.assembly} was not found under ${host.outputDir}.`);
  return assembly;
}

/**
 * Builds a .NET host, then runs its assembly detached from the project folder in Development on its URL (a detached
 * `dotnet run` exits at once on Windows).
 */
function startHost(system, root, host, chosenSeqUrl, layout) {
  const logFile = path.join(system.stateDir, host.logFile);
  const dotnet = resolveDotnet(system.env, system.exists);
  const assembly = buildHost(system, root, dotnet, host, layout.slot);
  const url = layout.urls[host.service.name];
  const handle = system.spawnDetached(dotnet, [assembly], {
    cwd: path.join(root, host.projectDir),
    env: hostEnvironment(system.env, url, chosenSeqUrl, layout.overrides[host.service.name]),
    logFile,
  });
  const entry = { name: host.service.name, pid: handle.pid, image: host.service.image };
  return { entry, handle, logFile, label: host.label, url: `${url}${host.healthPath}`, timeoutMs: host.timeoutMs };
}

function webStartDecision(system, root, noWeb, slot) {
  const manifestPath = path.join(root, FRONTEND_MANIFEST);
  const manifest = system.exists(manifestPath) ? JSON.parse(system.readText(manifestPath)) : null;
  const hasNodeModules = system.exists(path.join(root, NODE_MODULES_DIR)) || system.exists(path.join(root, FRONTEND_WORKSPACE, NODE_MODULES_DIR));
  const decision = webDecision({ noWeb, manifest, hasNodeModules, startScript: FRONTEND_START_SCRIPT });
  if (decision.start && slot !== Slots.default && !system.exists(path.join(root, ANGULAR_CLI))) {
    return { start: false, reason: `${ANGULAR_CLI} is missing; run npm install at the repo root first` };
  }
  return decision;
}

function startWeb(system, root, urls) {
  const logFile = path.join(system.stateDir, LOG_FILES.web);
  const nodeDir = resolveNodeDir(system.env, system.exists);
  if (!nodeDir) system.warn('the private Node folder was not found; the web app starts with the Node on PATH.');
  const command = `${NPM_SHIM} ${FRONTEND_START_SCRIPT} --workspace ${FRONTEND_WORKSPACE}`;
  const handle = system.spawnDetached(CMD, ['/d', '/s', '/c', command], { cwd: root, env: webEnvironment(system.env, nodeDir), logFile });
  const entry = { name: Services.web.name, pid: handle.pid, image: Services.web.image };
  return { entry, handle, logFile, label: 'The web app', url: `${urls.web}${WEB_HEALTH_PATH}`, timeoutMs: Timeouts.web };
}

/**
 * The web app of a slot: a development build of the current tree into the slot folder (the default stack's ng serve
 * and its hot reload are left alone), its config.json pointed at the slot's API and Attachment service, served by
 * static-server.mjs on the slot's web port.
 */
function startSlotWeb(system, root, urls) {
  const logFile = path.join(system.stateDir, LOG_FILES.web);
  const node = system.nodeExecutable;
  const workspace = path.join(root, FRONTEND_WORKSPACE);
  const outputDir = path.join(system.stateDir, SLOT_WEB_DIR);
  system.log(`Building the web app into ${outputDir} (a frozen development build for this slot).`);
  const steps = [
    [path.join(workspace, ICONS_SUBSET_SCRIPT), ...ICONS_SUBSET_ARGUMENTS],
    [path.join(root, ANGULAR_CLI), ...ANGULAR_BUILD_ARGUMENTS, outputDir],
  ];
  for (const step of steps) {
    const result = system.runSync(node, step, { cwd: workspace });
    if (result.code !== 0) throw new RunLocalError(`The web app build failed:\n${result.output.slice(-LOG_TAIL_CHARACTERS)}`);
  }
  const browserDir = path.join(outputDir, WEB_BROWSER_DIR);
  const configFile = path.join(browserDir, WEB_RUNTIME_CONFIG);
  if (!system.exists(configFile)) throw new RunLocalError(`The web app built, but ${WEB_RUNTIME_CONFIG} was not found under ${browserDir}.`);
  system.writeText(configFile, `${JSON.stringify(slotWebConfig(JSON.parse(system.readText(configFile)), urls), null, 2)}\n`);
  const args = [path.join(root, STATIC_SERVER_SCRIPT), ServerFlags.dir, browserDir, ServerFlags.port, new URL(urls.web).port];
  const handle = system.spawnDetached(node, args, { cwd: root, env: system.env, logFile });
  const entry = { name: Services.slotWeb.name, pid: handle.pid, image: Services.slotWeb.image };
  return { entry, handle, logFile, label: 'The web app', url: `${urls.web}${WEB_HEALTH_PATH}`, timeoutMs: Timeouts.slotWeb };
}

function record(system, processes) {
  system.writeText(statePath(system), serializeState(processes, new Date(system.now()).toISOString()));
}

/** The hint every refusal ends with: another slot runs a second stack next to the one in the way. */
function nextSlotHint(slot) {
  return slot < Slots.max ? `, or run a second stack next to it with run-local.ps1 -Slot ${slot + 1}` : '';
}

async function refuseBusyPorts(system, plan, slot) {
  const busy = [];
  for (const url of portsToCheck(plan, slotUrls(slot))) {
    if ((await system.httpStatus(url)) !== null) busy.push(url);
  }
  if (busy.length > 0) {
    throw new RunLocalError(
      `Something already answers on ${busy.join(' and ')}; stop it first (run-local.ps1 -Stop${slotArgument(slot)} stops only what run-local started)${nextSlotHint(slot)}.`,
    );
  }
}

/** Decides what starts: the API always, the Attachment service and the web app when they can (warning otherwise). */
function decide(system, root, { noWeb, noAttachments, slot }) {
  const web = webStartDecision(system, root, noWeb, slot);
  const attachments = attachmentsDecision({ noAttachments, projectExists: system.exists(path.join(root, ATTACHMENTS_PROJECT_FILE)) });
  if (!attachments.start) system.warn(`Attachment service not started: ${attachments.reason}`);
  if (!web.start) system.warn(`web not started: ${web.reason}`);
  return startPlan({ attachments: attachments.start, web: web.start });
}

/** Starts the planned services in order, recording each PID as soon as it exists; returns the started handles. */
function launch(system, root, plan, chosenSeq, started, layout) {
  const launched = [];
  for (const name of plan) {
    const service = launchOne(system, root, name, chosenSeq, layout);
    launched.push(service);
    started.push(service.entry);
    record(system, started);
  }
  return launched;
}

function launchOne(system, root, name, chosenSeq, layout) {
  if (name !== Services.web.name) return startHost(system, root, Hosts[name], chosenSeq, layout);
  return layout.slot === Slots.default ? startWeb(system, root, layout.urls) : startSlotWeb(system, root, layout.urls);
}

/**
 * The ports, configuration overrides and database of a slot (slot 0: the default ports and no overrides). A slot's
 * database is the configured one (ConnectionStrings__Starter, else the development settings) renamed for the slot.
 */
function slotLayout(system, root, slot) {
  const urls = slotUrls(slot);
  if (slot === Slots.default) return { slot, urls, overrides: slotOverrides({ slot, urls }) };
  const settings = path.join(root, API_DEVELOPMENT_SETTINGS);
  const configured = system.env[CONNECTION_STRING_ENV] ?? (system.exists(settings) ? developmentConnectionString(system.readText(settings)) : null);
  const storageRoot = path.join(system.stateDir, SLOT_STORAGE_DIR);
  const overrides = slotOverrides({ slot, urls, connectionString: configured, storageRoot });
  system.log(`Slot ${slot}: API ${urls.api}, Attachment service ${urls.attachments}, web ${urls.web}, state ${system.stateDir}.`);
  return { slot, urls, overrides };
}

/**
 * Starts the local stack and returns the summary lines. On a failed start it stops what it started, then throws.
 * @param {ReturnType<import('./system.mjs').createSystem>} system
 * @param {{ root: string, noWeb: boolean, noSeq: boolean, noAttachments?: boolean }} options
 */
export async function start(baseSystem, { root, noWeb, noSeq, noAttachments = false, slot = Slots.default }) {
  const system = forSlot(baseSystem, slot);
  const running = readState(system)?.processes.filter((entry) => isAlive(system, entry)) ?? [];
  if (running.length > 0) {
    const pids = running.map((entry) => `${entry.name} PID ${entry.pid}`).join(', ');
    throw new RunLocalError(`run-local is already running (${pids}); run run-local.ps1 -Stop${slotArgument(slot)} first${nextSlotHint(slot)}.`);
  }
  const plan = decide(system, root, { noWeb, noAttachments, slot });
  await refuseBusyPorts(system, plan, slot);
  ensureSecrets(system, root, plan.includes(Services.attachments.name));
  startLocalDb(system);
  const chosenSeq = await prepareSeq(system, noSeq);
  const layout = slotLayout(system, root, slot);

  const started = [];
  try {
    const launched = launch(system, root, plan, chosenSeq, started, layout);
    for (const service of launched) await waitHealthy(system, service);
    return summary(system, root, { plan, seq: chosenSeq, layout });
  } catch (error) {
    if (started.length > 0) for (const line of stop(baseSystem, slot)) system.log(line);
    throw error;
  }
}

function summary(system, root, { plan, seq, layout }) {
  const { urls, slot } = layout;
  const secrets = path.join(root, LOCAL_SECRETS_FILE);
  const user = system.exists(secrets) ? breakGlassUser(system.readText(secrets)) : null;
  const log = (name) => path.join(system.stateDir, LOG_FILES[name]);
  const has = (name) => plan.includes(name);
  const attachments = Hosts.attachments;
  return [
    'The local stack is up.',
    `  API      ${urls.api}  (health ${urls.api}${Hosts.api.healthPath}, log ${log(Services.api.name)})`,
    has(Services.attachments.name)
      ? `  Files    ${urls.attachments}  (Attachment service; health ${urls.attachments}${attachments.healthPath}, log ${log(Services.attachments.name)})`
      : '  Files    Attachment service not started',
    has(Services.web.name) ? `  Web      ${urls.web}  (log ${log(Services.web.name)})` : '  Web      not started',
    seq ? `  Seq      ${seq}` : '  Seq      not used',
    `  Sign in  ${user ?? '(unknown)'}, password in ${LOCAL_SECRETS_FILE} (Identity:BreakGlass:Password)`,
    ...(slot === Slots.default ? [] : [`  Database ${layout.overrides.api[CONNECTION_STRING_ENV] ?? '(as configured)'}`]),
    `Stop with: powershell -NoProfile -ExecutionPolicy Bypass -File run-local.ps1 -Stop${slotArgument(slot)}`,
  ];
}

/** The CLI entry: returns the exit code. */
export async function main(options, system) {
  try {
    const lines = options.stop ? stop(system, options.slot) : await start(system, options);
    for (const line of lines) system.log(line);
    return ExitCode.ok;
  } catch (error) {
    if (!(error instanceof RunLocalError)) throw error;
    system.warn(error.message);
    return ExitCode.failed;
  }
}
