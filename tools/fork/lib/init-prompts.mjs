// The interactive init (init.ps1 without -ConfigFile or -Minimal): asks for the same fields as the init config and
// returns the same object, so both modes share one validation (init-config.mjs). Answer parsing is pure; asking goes
// through an injected `ask(question) => Promise<string | null>` (null: input closed).
import { createInterface } from 'node:readline/promises';
import { UsageError } from './args.mjs';
import {
  COLOR_PREFIX,
  DEFAULT_LOGIN_MODES,
  LOGIN_MODES,
  OPTIONAL_MODULE_IDS,
  checkInitConfig,
  refusalForColor,
  refusalForLoginModes,
  refusalForLogo,
  refusalForName,
  refusalForPrefixFormat,
} from './init-config.mjs';

/** How often a question is asked again after an invalid answer before init gives up. */
export const MAX_ATTEMPTS = 5;
const ANSWER_SEPARATOR = /[\s,]+/;
const YES = Object.freeze(['y', 'yes']);
const NO = Object.freeze(['n', 'no']);
const FIRST_CHOICE_NUMBER = 1;
const DIGITS = /^\d+$/;

const ok = (value) => ({ value });
const fail = (error) => ({ error });

/** Parses a yes/no answer; empty takes `defaultValue`. */
export function parseYesNo(answer, defaultValue) {
  const text = answer.trim().toLowerCase();
  if (text.length === 0) return ok(defaultValue);
  if (YES.includes(text)) return ok(true);
  if (NO.includes(text)) return ok(false);
  return fail('Answer y or n.');
}

/** Parses a product prefix (upper-cased). */
export function parsePrefix(answer) {
  const prefix = answer.trim().toUpperCase();
  const refusal = refusalForPrefixFormat(prefix);
  return refusal ? fail(refusal) : ok(prefix);
}

/** Parses a product name. */
export function parseName(field, answer) {
  const refusal = refusalForName(field, answer);
  return refusal ? fail(refusal) : ok(answer.trim());
}

/** Parses a colour, with or without the leading '#'. */
export function parseColor(answer) {
  const text = answer.trim();
  const color = (text.startsWith(COLOR_PREFIX) ? text : `${COLOR_PREFIX}${text}`).toLowerCase();
  const refusal = refusalForColor(color);
  return refusal ? fail(refusal) : ok(color);
}

/** Parses a logo path; empty means no logo. */
export function parseLogoPath(answer, resolve) {
  const text = answer.trim();
  if (text.length === 0) return ok(null);
  const file = resolve(text);
  const refusal = refusalForLogo(file);
  return refusal ? fail(refusal) : ok(file);
}

/**
 * Parses login modes: names or 1-based numbers of LOGIN_MODES, separated by commas or spaces; empty takes
 * DEFAULT_LOGIN_MODES. The result is in LOGIN_MODES order.
 */
export function parseLoginModes(answer) {
  const words = answer.trim().toLowerCase().split(ANSWER_SEPARATOR).filter((word) => word.length > 0);
  if (words.length === 0) return ok([...DEFAULT_LOGIN_MODES]);
  const modes = words.map((word) => (DIGITS.test(word) ? LOGIN_MODES[Number(word) - FIRST_CHOICE_NUMBER] ?? word : word));
  const refusal = refusalForLoginModes(modes);
  return refusal ? fail(refusal) : ok(LOGIN_MODES.filter((mode) => modes.includes(mode)));
}

/** Parses a connection string; empty means "skip the database step". */
export function parseConnectionString(answer) {
  const text = answer.trim();
  return ok(text.length === 0 ? null : text);
}

/** Asks until `parse` accepts the answer; throws UsageError when input closes or after MAX_ATTEMPTS. */
async function askUntilValid(ask, io, question, parse) {
  for (let attempt = 0; attempt < MAX_ATTEMPTS; attempt += 1) {
    const answer = await ask(question);
    if (answer === null) throw new UsageError('Input closed before init had its answers; use -ConfigFile for a non-interactive run.');
    const result = parse(answer);
    if (!('error' in result)) return result.value;
    io.log(`  ${result.error}`);
  }
  throw new UsageError(`No valid answer after ${MAX_ATTEMPTS} attempts.`);
}

/**
 * Asks every init question and returns the normalized, validated init config.
 * @param {(question: string) => Promise<string | null>} ask
 * @param {{ log: (line: string) => void }} io
 * @param {{ resolve: (file: string) => string, skipDatabase: boolean }} options
 */
export async function promptInitConfig(ask, io, options) {
  const nameEn = await askUntilValid(ask, io, 'Project name (English): ', (answer) => parseName('nameEn', answer));
  const nameAr = await askUntilValid(ask, io, 'Project name (Arabic): ', (answer) => parseName('nameAr', answer));
  const prefix = await askUntilValid(ask, io, 'Code prefix (2-16 characters, A-Z then A-Z or 0-9, e.g. ACME): ', parsePrefix);
  const logoPath = await askUntilValid(ask, io, 'Logo file (.svg/.png/.webp/.jpg, empty for none): ', (answer) => parseLogoPath(answer, options.resolve));
  const primaryColor = await askUntilValid(ask, io, 'Primary colour (#rrggbb): ', parseColor);
  const choices = LOGIN_MODES.map((mode, index) => `${index + FIRST_CHOICE_NUMBER}=${mode}`).join(' ');
  const loginModes = await askUntilValid(ask, io, `Login modes, one or more (${choices}; empty: ${DEFAULT_LOGIN_MODES.join(', ')}): `, parseLoginModes);
  const optionalModules = {};
  for (const id of OPTIONAL_MODULE_IDS) {
    optionalModules[id] = await askUntilValid(ask, io, `Keep optional module '${id}'? [Y/n]: `, (answer) => parseYesNo(answer, true));
  }
  const connectionString = options.skipDatabase
    ? null
    : await askUntilValid(ask, io, 'SQL Server connection string for the local database (empty: skip): ', parseConnectionString);
  const raw = { prefix, nameEn, nameAr, primaryColor, logoPath, loginModes, optionalModules, database: { connectionString, skip: connectionString === null } };
  const checked = checkInitConfig(raw, process.cwd());
  if (checked.errors) throw new UsageError(checked.errors.join(' '));
  return checked.config;
}

/** A console `ask` on stdin/stdout, and its close. Answers are null once stdin ends. */
export function createConsoleAsk() {
  const reader = createInterface({ input: process.stdin, output: process.stdout });
  let closed = false;
  // A pending question never settles when stdin ends, so every question races the close.
  const whenClosed = new Promise((resolve) => {
    reader.once('close', () => {
      closed = true;
      resolve(null);
    });
  });
  const ask = async (question) => {
    if (closed) return null;
    try {
      return await Promise.race([reader.question(question), whenClosed]);
    } catch {
      return null;
    }
  };
  return { ask, close: () => reader.close() };
}
