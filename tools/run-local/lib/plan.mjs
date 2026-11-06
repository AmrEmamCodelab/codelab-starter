// Pure decisions of run-local: argument parsing, the recorded-state format, toolchain resolution, which services to
// start and how a health poll ends. No process or file-system access here, so every rule is unit-tested.
import path from 'node:path';
import {
  CONNECTION_STRING_ENV,
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

/** Parses the CLI flags into { stop, noWeb, noSeq, noAttachments, root, help }. */
export function parseArguments(argv, defaultRoot) {
  const options = { stop: false, noWeb: false, noSeq: false, noAttachments: false, help: false, root: defaultRoot };
  const switches = {
    [Flags.stop]: 'stop', [Flags.noWeb]: 'noWeb', [Flags.noSeq]: 'noSeq', [Flags.noAttachments]: 'noAttachments', [Flags.help]: 'help',
  };
  for (let index = 0; index < argv.length; index += 1) {
    const flag = argv[index];
    if (switches[flag]) {
      options[switches[flag]] = true;
    } else if (flag === Flags.root && argv[index + 1]) {
      options.root = path.resolve(argv[index + 1]);
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

/** The environment of a .NET host: Development on `url`, plus the Seq URL when one was chosen. */
export function hostEnvironment(env, url, chosenSeqUrl) {
  const result = { ...env, [ENVIRONMENT_ENV]: DEVELOPMENT, [URLS_ENV]: url };
  if (chosenSeqUrl) result[SEQ_SERVER_URL_ENV] = chosenSeqUrl;
  return result;
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

/** The environment of the web process: the private Node first on PATH. */
export function webEnvironment(env, nodeDir) {
  const pathKey = Object.keys(env).find((key) => key.toUpperCase() === PATH_ENV) ?? PATH_ENV;
  return { ...env, [pathKey]: prependPath(env[pathKey], nodeDir) };
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

/**
 * Decides whether the Attachment service starts: it needs its project (a fork may have removed the service).
 * @returns {{ start: boolean, reason?: string }}
 */
export function attachmentsDecision({ noAttachments, projectExists }) {
  if (noAttachments) return { start: false, reason: 'skipped (-NoAttachments)' };
  if (!projectExists) return { start: false, reason: 'the Attachment service project does not exist in this repository' };
  return { start: true };
}

/** The services to start, in start and health-check order (the API always; the others when decided). */
export function startPlan({ attachments, web }) {
  const wanted = { api: true, attachments, web };
  return START_ORDER.filter((name) => wanted[name]);
}

/** The URLs that must be free before a start: one per service in the plan. */
export function portsToCheck(plan) {
  return plan.map((name) => Urls[name]);
}

/**
 * True when the local secrets must be (re)seeded: the API file is missing, or the Attachment service starts and its
 * file is missing or the API file lacks the attachment keys (an API file from before the service or from before the
 * dedicated upload-ticket ring). The seeding script
 * only adds missing keys, so existing values are never changed.
 */
export function secretsNeedSeeding({ apiSecretsText, serviceSecretsExists, withAttachments }) {
  if (apiSecretsText === null || apiSecretsText === undefined) return true;
  if (!withAttachments) return false;
  if (!serviceSecretsExists) return true;
  try {
    const security = JSON.parse(apiSecretsText)?.Codelab?.Security;
    return (
      !security?.Tickets?.ActiveKeyId ||
      !security?.UploadTickets?.ActiveKeyId ||
      !security?.Uploads?.ActiveKeyId ||
      !security?.ServiceAssertions?.Client?.PrivateKeyPem
    );
  } catch {
    return true;
  }
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
