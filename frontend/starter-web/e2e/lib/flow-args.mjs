// Command-line parsing and credential resolution of e2e/flows.mjs (pure: argv, env and file text in; options out).
import os from 'node:os';
import path from 'node:path';
import {
  ALL_FLOWS,
  ALL_FLOWS_TOKEN,
  BREAK_GLASS_PATH,
  CredentialEnv,
  DEFAULT_FLOW_OUTPUT_FOLDER,
  DEFAULT_MAIL_FOLDER,
  DEFAULT_SECRETS_FILE,
} from './flow-constants.mjs';

export const FLOW_USAGE = `Usage: node e2e/flows.mjs --base-url <url> [options]

Runs the scripted phase-5 user flows in the installed Microsoft Edge against a running app (run-local.ps1) and
writes a step log (JSON + Markdown) with one screenshot per step.

  --base-url <url>      the running web app, for example http://localhost:4280
  --flows <list>        ${ALL_FLOWS.join(', ')} or ${ALL_FLOWS_TOKEN} (default ${ALL_FLOWS_TOKEN})
  --mail-dir <dir>      the API's e-mail pickup folder (Notifications:Email:PickupDirectory; default %TEMP%\\${DEFAULT_MAIL_FOLDER})
  --secrets-file <file> local secrets JSON holding Identity:BreakGlass (default ${DEFAULT_SECRETS_FILE})
  --out-dir <dir>       step log and screenshots (default %TEMP%/${DEFAULT_FLOW_OUTPUT_FOLDER}/<timestamp>)
  --headed              show the browser
  --help                this text

The admin user and password come from ${CredentialEnv.User} / ${CredentialEnv.PasswordVar} when set, else from the
secrets file. The password is never printed or written to the report.`;

const LIST_SEPARATOR = ',';
const TIMESTAMP_UNSAFE = /[:.]/g;
const SWITCHES = new Set(['--headed', '--help']);
const VALUE_FLAGS = new Set(['--base-url', '--flows', '--mail-dir', '--secrets-file', '--out-dir']);

/** Splits argv into { values: Map<flag, string>, switches: Set<flag> } or { error }. */
export function tokenizeFlowArgs(argv) {
  const values = new Map();
  const switches = new Set();
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (SWITCHES.has(argument)) {
      switches.add(argument);
      continue;
    }
    if (!VALUE_FLAGS.has(argument)) return { error: `Unknown option: ${argument}` };
    const value = argv[index + 1];
    if (value === undefined || VALUE_FLAGS.has(value) || SWITCHES.has(value)) return { error: `${argument} needs a value.` };
    if (values.has(argument)) return { error: `${argument} is given twice.` };
    values.set(argument, value);
    index += 1;
  }
  return { values, switches };
}

/** The flows a --flows value selects, in the canonical order, or { error }. */
export function selectFlows(value) {
  if (value === undefined || value.trim() === ALL_FLOWS_TOKEN) return { flows: [...ALL_FLOWS] };
  const asked = value
    .split(LIST_SEPARATOR)
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
  const unknown = asked.filter((name) => !ALL_FLOWS.includes(name));
  if (asked.length === 0 || unknown.length > 0) return { error: `--flows takes ${ALL_FLOWS.join(', ')} or ${ALL_FLOWS_TOKEN}; got ${value}.` };
  return { flows: ALL_FLOWS.filter((name) => asked.includes(name)) };
}

/** Parses the command line: { options } or { error } or { help: true }. */
export function parseFlowArgs(argv, { tempDir = os.tmpdir(), now = new Date(), cwd = process.cwd() } = {}) {
  const tokens = tokenizeFlowArgs(argv);
  if (tokens.error) return { error: tokens.error };
  const { values, switches } = tokens;
  if (switches.has('--help')) return { help: true };
  const baseUrl = values.get('--base-url');
  if (!baseUrl) return { error: '--base-url is required.' };
  if (!URL.canParse(baseUrl)) return { error: `--base-url is not a URL: ${baseUrl}` };
  const selection = selectFlows(values.get('--flows'));
  if (selection.error) return { error: selection.error };
  const stamp = now.toISOString().replace(TIMESTAMP_UNSAFE, '-');
  return {
    options: {
      baseUrl,
      flows: selection.flows,
      mailDir: path.resolve(cwd, values.get('--mail-dir') ?? path.join(tempDir, DEFAULT_MAIL_FOLDER)),
      secretsFile: path.resolve(cwd, values.get('--secrets-file') ?? DEFAULT_SECRETS_FILE),
      outDir: path.resolve(cwd, values.get('--out-dir') ?? path.join(tempDir, DEFAULT_FLOW_OUTPUT_FOLDER, stamp)),
      headed: switches.has('--headed'),
      runStamp: stamp,
    },
  };
}

/**
 * The admin credentials: the environment variables win, else Identity:BreakGlass in the secrets JSON text.
 * Returns { user, password } or { error } (the error never contains the password).
 */
export function resolveCredentials(env, readSecretsText) {
  let user = env[CredentialEnv.User] || null;
  let adminPassword = env[CredentialEnv.PasswordVar] || null;
  if (user === null || adminPassword === null) {
    let breakGlass = null;
    try {
      breakGlass = BREAK_GLASS_PATH.reduce((node, key) => node?.[key], JSON.parse(readSecretsText()));
    } catch (error) {
      return { error: `No admin credentials: set ${CredentialEnv.User}/${CredentialEnv.PasswordVar} or create the local secrets file (tools/devseed/New-LocalSecrets.ps1). (${error.message})` };
    }
    user ??= breakGlass?.UserName ?? null;
    adminPassword ??= breakGlass?.Password ?? null;
  }
  if (!user || !adminPassword) return { error: `No admin credentials: ${BREAK_GLASS_PATH.join(':')} has no UserName/Password and ${CredentialEnv.User}/${CredentialEnv.PasswordVar} are not set.` };
  return { user, password: adminPassword };
}
