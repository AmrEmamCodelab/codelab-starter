// Pure decisions of run-local: argument parsing, the recorded-state format, toolchain resolution, which services to
// start and how a health poll ends. No process or file-system access here, so every rule is unit-tested.
import path from 'node:path';
import {
  CONNECTION_STRING_ENV,
  DATABASE_KEYS,
  HealthVerdict,
  Hosts,
  LOCALHOST,
  LoginMode,
  NG_ANALYTICS_ENV,
  NG_ANALYTICS_OFF,
  OptionalModule,
  SEQ_HEALTH_PATH,
  SEQ_NAME,
  Services,
  WEB_HEALTH_PATH,
  LOGIN_PATH,
  Ports,
  SLOT_DATABASE_SUFFIX,
  SLOT_DIR_PREFIX,
  SLOT_STORAGE_DIR,
  SSO_CALLBACK_PATH,
  SlotSettings,
  Slots,
  TicketRingState,
  DEVELOPMENT,
  LOCALDB_INSTANCE,
  DOTNET_ENV,
  DOTNET_ON_PATH,
  ENVIRONMENT_ENV,
  Flags,
  NODE_DIR_ENV,
  PATH_ENV,
  PRIVATE_DOTNET,
  PRIVATE_NODE_DIR,
  SEQ_SERVER_URL_ENV,
  SEQ_URL_ENV,
  START_ORDER,
  URLS_ENV,
  Urls,
} from './constants.mjs';

/** Bad arguments (exit 2). */
export class UsageError extends Error {}

/** The slot number of `--slot <n>`: an integer from 0 to Slots.max, else a UsageError. */
export function parseSlot(text) {
  const slot = /^\d+$/.test(String(text ?? '')) ? Number(text) : Number.NaN;
  if (!Number.isInteger(slot) || slot < Slots.default || slot > Slots.max) {
    throw new UsageError(`${Flags.slot} needs a number from ${Slots.default} to ${Slots.max}, not '${text ?? ''}'.`);
  }
  return slot;
}

/** Parses the CLI flags into { stop, noWeb, noSeq, noAttachments, noGateway, noOps, root, slot, help }. */
export function parseArguments(argv, defaultRoot) {
  const options = {
    stop: false, noWeb: false, noSeq: false, noAttachments: false, noGateway: false, noOps: false, help: false, root: defaultRoot, slot: Slots.default,
  };
  const switches = {
    [Flags.stop]: 'stop', [Flags.noWeb]: 'noWeb', [Flags.noSeq]: 'noSeq', [Flags.noAttachments]: 'noAttachments',
    [Flags.noGateway]: 'noGateway', [Flags.noOps]: 'noOps', [Flags.help]: 'help',
  };
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    if (switches[flag]) {
      options[switches[flag]] = true;
    } else if (flag === Flags.root && argv[index + 1]) {
      options.root = path.resolve(argv[index + 1]);
      index += 1;
    } else if (flag === Flags.slot) {
      options.slot = parseSlot(argv[index + 1]);
      index += 1;
    } else {
      throw new UsageError(`Unknown argument '${flag}'.`);
    }
  }
  return options;
}

/** The dotnet executable: CODELAB_DOTNET, then the private SDK, then dotnet on PATH. */
export function resolveDotnet(env, exists) {
  if (env[DOTNET_ENV]) return env[DOTNET_ENV];
  return exists(PRIVATE_DOTNET) ? PRIVATE_DOTNET : DOTNET_ON_PATH;
}

/** The private Node folder (CODELAB_NODE_DIR, then the private toolchain), or null when neither exists. */
export function resolveNodeDir(env, exists) {
  const candidate = env[NODE_DIR_ENV] || PRIVATE_NODE_DIR;
  return exists(candidate) ? candidate : null;
}

/** The PATH value with `dir` first (Windows separator), unchanged when dir is null. */
export function prependPath(currentPath, dir) {
  if (!dir) return currentPath ?? '';
  return currentPath ? `${dir}${path.delimiter}${currentPath}` : dir;
}

/** The Seq URL to probe: CODELAB_SEQ_URL, else the default local port. */
export function seqUrl(env) {
  return env[SEQ_URL_ENV] || Urls.seqDefault;
}

/**
 * True when the API uses the default LocalDB instance: no connection-string override, or one that names it. With an
 * override on another server run-local leaves that instance alone (it never starts it).
 */
export function usesDefaultLocalDb(env) {
  const override = env[CONNECTION_STRING_ENV];
  return !override || override.toLowerCase().includes(LOCALDB_INSTANCE.toLowerCase());
}

/**
 * The environment of a .NET host: Development on `url`, plus the Seq URL when one was chosen, plus the slot's
 * configuration overrides (none for the default slot).
 */
export function hostEnvironment(env, url, chosenSeqUrl, overrides = {}) {
  const result = { ...env, [ENVIRONMENT_ENV]: DEVELOPMENT, [URLS_ENV]: url };
  if (chosenSeqUrl) result[SEQ_SERVER_URL_ENV] = chosenSeqUrl;
  return { ...result, ...overrides };
}

/** The environment of the API process (http://localhost:5180). */
export function apiEnvironment(env, chosenSeqUrl) {
  return hostEnvironment(env, Urls.api, chosenSeqUrl);
}

/** The environment of the Attachment service process (http://localhost:5185). */
export function attachmentsEnvironment(env, chosenSeqUrl) {
  return hostEnvironment(env, Urls.attachments, chosenSeqUrl);
}

/** The API assembly among the build output's target-framework folders (the newest framework wins), or null. */
export function apiAssemblyPath(frameworkDirs, outputDir, assembly, exists) {
  const candidates = [...frameworkDirs].sort((left, right) => right.localeCompare(left, 'en', { numeric: true })).map((dir) => path.join(outputDir, dir, assembly));
  return candidates.find((candidate) => exists(candidate)) ?? null;
}

/**
 * The environment of the web process: the private Node first on PATH and Angular CLI analytics off, so ng serve
 * never waits on its first-run prompt in a detached process.
 */
export function webEnvironment(env, nodeDir) {
  const pathKey = Object.keys(env).find((key) => key.toUpperCase() === PATH_ENV) ?? PATH_ENV;
  return { ...env, [pathKey]: prependPath(env[pathKey], nodeDir), [NG_ANALYTICS_ENV]: NG_ANALYTICS_OFF };
}

/**
 * Decides whether the web app can start: it needs the workspace manifest with a start script and installed
 * dependencies (run-local never installs).
 * @returns {{ start: boolean, reason?: string }}
 */
export function webDecision({ noWeb, manifest, hasNodeModules, startScript }) {
  if (noWeb) return { start: false, reason: 'skipped (-NoWeb)' };
  if (!manifest) return { start: false, reason: 'the frontend workspace does not exist yet' };
  if (!manifest.scripts?.[startScript]) return { start: false, reason: `the frontend has no "${startScript}" script` };
  if (!hasNodeModules) return { start: false, reason: 'node_modules is missing; run npm install at the repo root first' };
  return { start: true };
}

/** Every login mode and optional module on: a manifest without loginModes or optionalModules (a pre-init tree). */
const ALL_LOGIN_MODES = Object.freeze(Object.values(LoginMode));

/**
 * What the fork chose at init, from the text of codelab.starter.json (null when the file is missing): the login modes
 * and which optional modules are on. A missing or unreadable file, a missing `loginModes` (or an empty list) and a
 * missing `optionalModules` (or a missing id in it) all mean "on", so a tree from before init runs everything. A
 * module listed in `removedModules` (any letter case) is off whatever `optionalModules` says.
 * @returns {{ loginModes: string[], optionalModules: Record<string, boolean> }}
 */
export function starterFeatures(manifestText) {
  let manifest = null;
  try {
    manifest = manifestText ? JSON.parse(manifestText) : null;
  } catch {
    manifest = null;
  }
  const modes = Array.isArray(manifest?.loginModes) && manifest.loginModes.length > 0
    ? manifest.loginModes.filter((mode) => typeof mode === 'string').map((mode) => mode.toLowerCase())
    : [...ALL_LOGIN_MODES];
  const removed = new Set((Array.isArray(manifest?.removedModules) ? manifest.removedModules : []).map((name) => String(name).toLowerCase()));
  const optionalModules = Object.fromEntries(
    Object.values(OptionalModule).map((id) => [id, manifest?.optionalModules?.[id] !== false && !removed.has(id)]),
  );
  return { loginModes: modes, optionalModules };
}

/**
 * Decides whether the Attachment service starts: the attachments module is on (codelab.starter.json), the project
 * exists (a fork may have removed the service) and -NoAttachments is not given.
 * @returns {{ start: boolean, reason?: string }}
 */
export function attachmentsDecision({ noAttachments, projectExists, features = starterFeatures(null) }) {
  if (noAttachments) return { start: false, reason: 'skipped (-NoAttachments)' };
  if (!features.optionalModules[OptionalModule.attachments]) return { start: false, reason: 'the attachments module is off in codelab.starter.json' };
  if (!projectExists) return { start: false, reason: 'the Attachment service project does not exist in this repository' };
  return { start: true };
}

/**
 * Decides whether the integration gateway starts: only when a login mode needs it (sso: the IdP token and JWKS relay)
 * or the sms module is on (the SMS relay), its project exists and -NoGateway is not given.
 * @returns {{ start: boolean, reason?: string }}
 */
export function gatewayDecision({ noGateway, projectExists, features = starterFeatures(null) }) {
  if (noGateway) return { start: false, reason: 'skipped (-NoGateway)' };
  const needed = features.loginModes.includes(LoginMode.sso) || features.optionalModules[OptionalModule.sms];
  if (!needed) return { start: false, reason: 'not needed: codelab.starter.json has no sso login mode and the sms module is off' };
  if (!projectExists) return { start: false, reason: 'the integration gateway project does not exist in this repository' };
  return { start: true };
}

/**
 * Decides whether the Ops Console API starts: the ops module is on, its project exists and -NoOps is not given.
 * @returns {{ start: boolean, reason?: string }}
 */
export function opsDecision({ noOps, projectExists, features = starterFeatures(null) }) {
  if (noOps) return { start: false, reason: 'skipped (-NoOps)' };
  if (!features.optionalModules[OptionalModule.ops]) return { start: false, reason: 'the ops module is off in codelab.starter.json' };
  if (!projectExists) return { start: false, reason: 'the Ops Console project does not exist in this repository' };
  return { start: true };
}

/** The services to start, in start and health-check order (the API always; the others when decided). */
export function startPlan({ attachments = false, gateway = false, ops = false, web = false }) {
  const wanted = { [Services.api.name]: true, [Services.attachments.name]: attachments, [Services.gateway.name]: gateway, [Services.ops.name]: ops, [Services.web.name]: web };
  return START_ORDER.filter((name) => wanted[name]);
}

/**
 * The real health endpoint of a planned service at the slot's URLs: the readiness path of each .NET host
 * (MapCodelabHealth: /health/ready, which runs the host's own readiness checks), the root of the web app, and Seq's
 * /health (pass the chosen Seq URL as `seq`).
 */
export function healthUrl(name, urls, seq = null) {
  if (name === SEQ_NAME) return `${seq}${SEQ_HEALTH_PATH}`;
  if (name === Services.web.name) return `${urls.web}${WEB_HEALTH_PATH}`;
  return `${urls[name]}${Hosts[name].healthPath}`;
}

/** The planned hosts that read S2S keys from their own local secrets file and do not have it (`hasFile(host)`). */
export function hostsMissingSecrets(plan, hasFile) {
  return plan.filter((name) => Hosts[name]?.secretsFile && !hasFile(Hosts[name]));
}

/**
 * The per-service health report: one line per checked service with its verdict and URL, and `ok` only when every
 * one is healthy. `results` are { name, url, outcome: 'ready' | 'exited' | 'timeout', timeoutMs, hint? }.
 * @returns {{ ok: boolean, lines: string[] }}
 */
export function healthReport(results) {
  const width = Math.max(0, ...results.map((result) => result.name.length));
  const lines = results.map((result) => {
    const healthy = result.outcome === 'ready';
    const verdict = healthy ? HealthVerdict.healthy : HealthVerdict.unhealthy;
    const why = healthy ? '' : result.outcome === 'exited' ? ': exited before it was healthy' : `: not healthy within ${result.timeoutMs / 1000} s`;
    const hint = !healthy && result.hint ? ` (${result.hint})` : '';
    return `  ${result.name.padEnd(width)}  ${verdict.padEnd(HealthVerdict.unhealthy.length)}  ${result.url}${why}${hint}`;
  });
  return { ok: results.every((result) => result.outcome === 'ready'), lines };
}

/** The URLs that must be free before a start: one per service in the plan (`urls` defaults to slot 0's). */
export function portsToCheck(plan, urls = Urls) {
  return plan.map((name) => urls[name]);
}

/** The local URLs of a slot: slot 0 is Urls; slot n moves each port up by n * Slots.portStep. */
export function slotUrls(slot) {
  if (slot === Slots.default) return Urls;
  const offset = slot * Slots.portStep;
  const at = (port) => `${LOCALHOST}:${port + offset}`;
  return Object.freeze({
    ...Urls, api: at(Ports.api), attachments: at(Ports.attachments), gateway: at(Ports.gateway), ops: at(Ports.ops), web: at(Ports.web),
  });
}

/** The state folder of a slot: the base folder for slot 0, `<base>\slot-<n>` otherwise. */
export function slotStateDir(baseDir, slot) {
  return slot === Slots.default ? baseDir : path.join(baseDir, `${SLOT_DIR_PREFIX}${slot}`);
}

/**
 * The attachment storage folder of a slot, under that slot's own state folder (`<state>\slot-<n>\attachments-storage`),
 * or null for slot 0, whose storage is the configured one (the local secrets) and is never touched by run-local.
 * run-local creates this folder before it starts the slot's Attachment service: the service's readiness check
 * (StorageReadinessCheck) reports a missing storage root as unhealthy.
 */
export function slotStorageRoot(slotStateDirectory, slot) {
  return slot === Slots.default ? null : path.join(slotStateDirectory, SLOT_STORAGE_DIR);
}

/**
 * The connection string of slot n: the same server with the database renamed to `<name>_Slot<n>` (idempotent), so a
 * slot never migrates or writes the default stack's database. Slot 0 and a string without a database name are kept.
 */
export function slotConnectionString(connectionString, slot) {
  if (slot === Slots.default || !connectionString) return connectionString;
  const suffix = `${SLOT_DATABASE_SUFFIX}${slot}`;
  return connectionString
    .split(';')
    .map((part) => {
      const separator = part.indexOf('=');
      if (separator < 0 || !DATABASE_KEYS.includes(part.slice(0, separator).trim().toLowerCase())) return part;
      const name = part.slice(separator + 1).trim();
      return name.endsWith(suffix) ? part : `${part.slice(0, separator)}=${name}${suffix}`;
    })
    .join(';');
}

/**
 * The configuration overrides of a slot's .NET hosts (empty for slot 0): the web origin, the SSO return URLs, the web
 * links, the Attachment service and gateway URLs (and the Ops readiness probes that name them), the database and the
 * attachment storage folder of that slot, and the Ops Console's upstream API. The gateway keeps its configured
 * upstreams (the local IdP is shared by every slot).
 */
export function slotOverrides({ slot, urls, connectionString, storageRoot }) {
  if (slot === Slots.default) return { api: {}, attachments: {}, gateway: {}, ops: {} };
  const api = {
    [SlotSettings.apiCorsOrigin]: urls.web,
    [SlotSettings.ssoRedirectUri]: `${urls.api}${SSO_CALLBACK_PATH}`,
    [SlotSettings.ssoPostLogoutRedirectUri]: `${urls.web}${LOGIN_PATH}`,
    [SlotSettings.webBaseUrl]: urls.web,
    [SlotSettings.activationLinkBaseUrl]: urls.web,
    [SlotSettings.attachmentsBaseUrl]: urls.attachments,
    [SlotSettings.gatewayBaseUrl]: urls.gateway,
    [SlotSettings.opsProbeAttachmentsUrl]: healthUrl(Services.attachments.name, urls),
    [SlotSettings.opsProbeGatewayUrl]: healthUrl(Services.gateway.name, urls),
  };
  const database = slotConnectionString(connectionString, slot);
  if (database) api[CONNECTION_STRING_ENV] = database;
  const attachments = { [SlotSettings.attachmentsCorsOrigin]: urls.web, [SlotSettings.attachmentsStorageRoot]: storageRoot };
  const ops = { [SlotSettings.opsConsoleApiBaseUrl]: `${urls.api}/` };
  return { api, attachments, gateway: {}, ops };
}

/** The API connection string from the development settings JSON text, or null. */
export function developmentConnectionString(settingsText) {
  try {
    return JSON.parse(settingsText)?.ConnectionStrings?.Starter ?? null;
  } catch {
    return null;
  }
}

/** The web runtime config of a slot: the built config.json pointed at the slot's API and Attachment service. */
export function slotWebConfig(config, urls) {
  return { ...config, apiBaseUrl: urls.api, origins: { ...config?.origins, api: [urls.api], attachment: [urls.attachments] } };
}

/**
 * True when the local secrets must be (re)seeded: the API file is missing, or the Attachment service starts and its
 * file is missing, the API file lacks the attachment keys (an API file from before the service or from before the
 * dedicated upload-ticket ring), or the service file has no ticket ring yet. The seeding script only adds missing
 * keys, so existing values are never changed (a wrong ring is reported by serviceTicketRingState, not reseeded).
 */
export function secretsNeedSeeding({ apiSecretsText, serviceSecretsExists, serviceSecretsText = null, withAttachments }) {
  if (apiSecretsText === null || apiSecretsText === undefined) return true;
  if (!withAttachments) return false;
  if (!serviceSecretsExists) return true;
  try {
    const security = JSON.parse(apiSecretsText)?.Codelab?.Security;
    return (
      !security?.Tickets?.ActiveKeyId ||
      !security?.UploadTickets?.ActiveKeyId ||
      !security?.Uploads?.ActiveKeyId ||
      !security?.ServiceAssertions?.Client?.PrivateKeyPem ||
      (serviceSecretsText !== null && serviceTicketRingState({ apiSecretsText, serviceSecretsText }) === TicketRingState.missing)
    );
  } catch {
    return true;
  }
}

/** The JSON object of a secrets file text, or null when it is not one. */
function parseSecrets(text) {
  try {
    const value = JSON.parse(text);
    return value !== null && typeof value === 'object' && !Array.isArray(value) ? value : null;
  } catch {
    return null;
  }
}

/** The keys of a ring (Keys[*]); an empty list when it has none. */
function ringKeys(ring) {
  return Array.isArray(ring?.Keys) ? ring.Keys.filter((key) => key !== null && typeof key === 'object') : [];
}

/** The ActiveKeyId and every Keys[*].KeyId of a ring. */
function ringKeyIds(ring) {
  return [ring?.ActiveKeyId, ...ringKeys(ring).map((key) => key.KeyId)].filter((id) => typeof id === 'string' && id !== '');
}

/** The Keys[*].MaterialBase64 of a ring (compared in memory only; never logged). */
function ringMaterials(ring) {
  return ringKeys(ring).map((key) => key.MaterialBase64).filter((material) => typeof material === 'string' && material !== '');
}

/** True when two rings share a key id or a key. */
function ringsOverlap(first, second) {
  const ids = new Set(ringKeyIds(first));
  const materials = new Set(ringMaterials(first));
  return ringKeyIds(second).some((id) => ids.has(id)) || ringMaterials(second).some((material) => materials.has(material));
}

/** True when two rings have the same active key and the same keys (id and material, in order). */
function ringsEqual(first, second) {
  const keys = (ring) => JSON.stringify(ringKeys(ring).map((key) => [key.KeyId ?? null, key.MaterialBase64 ?? null]));
  return first?.ActiveKeyId === second?.ActiveKeyId && keys(first) === keys(second);
}

/**
 * How the Attachment service file's ticket ring (Codelab:Security:Tickets) relates to the API file's rings. The
 * service must hold exactly the API's upload-ticket ring (Codelab:Security:UploadTickets) and never a key of the
 * API's own ticket ring (Codelab:Security:Tickets: SSO and activation tickets). A service file from before the
 * dedicated upload-ticket ring holds the API's own ring: every upload is refused with 401.
 */
export function serviceTicketRingState({ apiSecretsText, serviceSecretsText }) {
  const apiSecurity = parseSecrets(apiSecretsText)?.Codelab?.Security;
  const service = parseSecrets(serviceSecretsText);
  if (service === null) return TicketRingState.mismatch;
  const serviceRing = service.Codelab?.Security?.Tickets;
  if (ringKeyIds(serviceRing).length === 0) return TicketRingState.missing;
  if (ringsOverlap(apiSecurity?.Tickets, serviceRing)) return TicketRingState.apiTicketRing;
  return ringsEqual(apiSecurity?.UploadTickets, serviceRing) ? TicketRingState.matches : TicketRingState.mismatch;
}

/**
 * Decides what to do about Seq: use it when reachable, try the installed service when not, else warn.
 * @returns {{ action: 'use' | 'start-service' | 'skip', url: string, reason?: string }}
 */
export function seqDecision({ noSeq, url, reachable, serviceInstalled }) {
  if (noSeq) return { action: 'skip', url, reason: 'skipped (-NoSeq)' };
  if (reachable) return { action: 'use', url };
  if (serviceInstalled) return { action: 'start-service', url };
  return { action: 'skip', url, reason: `Seq is neither reachable at ${url} nor installed; the API logs to its console log only` };
}

/** The recorded state: { startedAt, processes: [{ name, pid, image }] }. Unreadable state is treated as none. */
export function parseState(text) {
  try {
    const state = JSON.parse(text);
    if (!Array.isArray(state?.processes)) return null;
    const processes = state.processes.filter((entry) => Number.isInteger(entry?.pid) && entry.pid > 0 && typeof entry.image === 'string');
    return { startedAt: state.startedAt ?? null, processes };
  } catch {
    return null;
  }
}

export function serializeState(processes, startedAt) {
  return `${JSON.stringify({ startedAt, processes }, null, 2)}\n`;
}

/**
 * True when `tasklist /FO CSV /NH` output for one PID shows the expected image (PID reuse guard: -Stop never kills
 * a process that is not the one it started).
 */
export function taskMatches(tasklistCsv, image) {
  const firstField = /^"([^"]+)"/.exec(String(tasklistCsv ?? '').trim());
  return Boolean(firstField) && firstField[1].toLowerCase() === image.toLowerCase();
}

/** Reads the break-glass user name from the local secrets JSON text, or null. */
export function breakGlassUser(secretsText) {
  try {
    return JSON.parse(secretsText)?.Identity?.BreakGlass?.UserName ?? null;
  } catch {
    return null;
  }
}

/**
 * Polls `probe` until it reports ready, the process exits or the deadline passes.
 * @param {{ probe: () => Promise<boolean>, exited: () => boolean, now: () => number, wait: (ms: number) => Promise<void>, timeoutMs: number, intervalMs: number }} io
 * @returns {Promise<'ready' | 'exited' | 'timeout'>}
 */
export async function pollUntilReady({ probe, exited, now, wait, timeoutMs, intervalMs }) {
  const deadline = now() + timeoutMs;
  for (;;) {
    if (await probe()) return 'ready';
    if (exited()) return 'exited';
    if (now() >= deadline) return 'timeout';
    await wait(intervalMs);
  }
}
