// The init config (ADR-003): the answers init.ps1 needs, from -ConfigFile JSON (CI) or from the interactive prompts
// (init-prompts.mjs), validated the same way. Also every name, path, limit and message template of the full init
// flow (init-steps.mjs). RENAME SAFETY: the starter-named paths below are written in the form a fork's renameMap
// finds (see constants.mjs), and callers still map them through the fork's renamer.
import { existsSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { PRODUCT_PREFIX_PATTERN, UTF8_BOM } from './constants.mjs';

/** Login modes as Auth:Modes names them (Starter.Modules.Identity AuthModeNames). */
export const LoginMode = Object.freeze({ basic: 'basic', activeDirectory: 'ad', sso: 'sso' });
export const LOGIN_MODES = Object.freeze([LoginMode.basic, LoginMode.activeDirectory, LoginMode.sso]);
/** The interactive default when the answer is empty. */
export const DEFAULT_LOGIN_MODES = Object.freeze([LoginMode.basic]);

/** Optional module ids (lower case); Identity and Access are required and never listed here. */
export const OptionalModuleId = Object.freeze({ attachments: 'attachments', portal: 'portal', sms: 'sms', ops: 'ops' });
export const OPTIONAL_MODULE_IDS = Object.freeze([
  OptionalModuleId.attachments, OptionalModuleId.portal, OptionalModuleId.sms, OptionalModuleId.ops,
]);
/** Optional modules that own local secrets (New-LocalSecrets.ps1 -SkipService takes these ids). */
export const SECRET_OWNING_MODULE_IDS = Object.freeze([OptionalModuleId.attachments, OptionalModuleId.sms, OptionalModuleId.ops]);

/** The top-level fields of an init config, in order. */
export const INIT_CONFIG_FIELDS = Object.freeze([
  'prefix', 'nameEn', 'nameAr', 'primaryColor', 'logoPath', 'loginModes', 'optionalModules', 'database',
]);
export const DATABASE_FIELDS = Object.freeze(['connectionString', 'skip']);

export const HEX_COLOR_PATTERN = /^#[0-9a-fA-F]{6}$/;
export const COLOR_PREFIX = '#';
export const MAX_NAME_LENGTH = 120;
export const LOGO_EXTENSIONS = Object.freeze(['.svg', '.png', '.webp', '.jpg', '.jpeg']);
export const MAX_LOGO_BYTES = 1024 * 1024;

/** Where init writes the login modes: the API settings, key Auth:Modes (AuthModesOptions). */
export const API_SETTINGS_FILE = 'backend/src/Starter.Api/appsettings.json';
export const AUTH_MODES_JSON_PATH = 'Auth:Modes';
/** The API's gitignored local secrets (New-LocalSecrets.ps1); its presence means the keys step already ran. */
export const API_LOCAL_SECRETS_FILE = 'backend/src/Starter.Api/appsettings.Development.local.json';

/** Branding: the web config block @codelab/ng-dga applies at run time, and the copied logo. */
export const WEB_CONFIG_FILE = 'frontend/starter-web/public/config.json';
export const BRANDING_JSON_PATH = 'branding';
export const LOGO_DIR = 'frontend/starter-web/public/branding';
export const LOGO_URL_FOLDER = 'branding';
export const LOGO_STEM = 'logo';

/** The scripts init calls (task init-keys-db) and their switches. */
export const LOCAL_DATABASE_SCRIPT = 'tools/devseed/Initialize-LocalDatabase.ps1';
export const ScriptSwitch = Object.freeze({
  force: '-Force',
  skipService: '-SkipService',
  connectionString: '-ConnectionString',
});
/** Initialize-LocalDatabase.ps1 exit codes. */
export const DatabaseExit = Object.freeze({ done: 0, refused: 1, usage: 2 });
export const LIST_JOINER = ',';

/** The contract modules init loads lazily (owned by tasks init-rename and module-removal). */
export const RENAME_DEFAULTS_MODULE = './rename-defaults.mjs';
export const RENAME_DEFAULTS_EXPORT = 'defaultRenameMap';
export const MODULE_REMOVAL_MODULE = './remove-module.mjs';
export const MODULE_REMOVAL_EXPORT = 'removeOptionalModules';

/** Commit messages of the init steps ({name} is the renamed solution token). */
export const InitCommit = Object.freeze({
  rename: 'chore(init): rename Starter to {name}',
  modules: 'chore(init): remove modules and packages',
  manifest: 'chore(init): record login modes and the fork manifest',
});
export const NAME_PLACEHOLDER = '{name}';

const isObject = (value) => value !== null && typeof value === 'object' && !Array.isArray(value);

function exactKeys(value, keys) {
  return isObject(value) && Object.keys(value).length === keys.length && keys.every((key) => key in value);
}

/**
 * Why a prefix's form is refused, or null. The reserved prefixes (prefix.mjs refusalForPrefix) are checked by the
 * rename step only: in a renamed fork the reserved list names the fork's own prefix, which a rerun repeats.
 */
export function refusalForPrefixFormat(prefix) {
  if (typeof prefix !== 'string' || !PRODUCT_PREFIX_PATTERN.test(prefix)) {
    return `prefix '${prefix}' is invalid: 2-16 characters, an upper-case letter A-Z first, then A-Z or 0-9.`;
  }
  return null;
}

/** Why a product name is refused, or null. */
export function refusalForName(field, value) {
  if (typeof value !== 'string' || value.trim().length === 0 || value.trim().length > MAX_NAME_LENGTH) {
    return `${field} must be 1-${MAX_NAME_LENGTH} characters.`;
  }
  return null;
}

/** Why a primary colour is refused, or null. */
export function refusalForColor(value) {
  if (typeof value !== 'string' || !HEX_COLOR_PATTERN.test(value)) return `primaryColor '${value}' must be a hex colour #rrggbb.`;
  return null;
}

/** Why a login-mode list is refused, or null. */
export function refusalForLoginModes(value) {
  if (!Array.isArray(value) || value.length === 0) return `loginModes must list at least one of ${LOGIN_MODES.join(', ')}.`;
  const unknown = value.filter((mode) => !LOGIN_MODES.includes(mode));
  if (unknown.length > 0) return `loginModes has unknown mode(s) ${unknown.map(String).join(', ')}; use ${LOGIN_MODES.join(', ')}.`;
  if (new Set(value).size !== value.length) return 'loginModes lists a mode more than once.';
  return null;
}

/** Why an optionalModules object is refused, or null. */
export function refusalForOptionalModules(value) {
  const valid = exactKeys(value, OPTIONAL_MODULE_IDS) && OPTIONAL_MODULE_IDS.every((id) => typeof value[id] === 'boolean');
  return valid ? null : `optionalModules must be { ${OPTIONAL_MODULE_IDS.map((id) => `${id}: bool`).join(', ')} }.`;
}

/**
 * Why a logo file is refused, or null (null path: no logo).
 * @param {string | null} logoPath an absolute path
 */
export function refusalForLogo(logoPath) {
  if (logoPath === null) return null;
  if (!existsSync(logoPath) || !statSync(logoPath).isFile()) return `logoPath '${logoPath}' is not a file.`;
  const extension = path.extname(logoPath).toLowerCase();
  if (!LOGO_EXTENSIONS.includes(extension)) return `logoPath must be one of ${LOGO_EXTENSIONS.join(', ')} (got '${extension}').`;
  if (statSync(logoPath).size > MAX_LOGO_BYTES) return `logoPath is larger than ${MAX_LOGO_BYTES} bytes.`;
  return null;
}

function databaseErrors(value) {
  const valid = exactKeys(value, DATABASE_FIELDS)
    && (value.connectionString === null || (typeof value.connectionString === 'string' && value.connectionString.trim().length > 0))
    && typeof value.skip === 'boolean';
  return valid ? [] : ['database must be { connectionString: non-empty string or null, skip: bool }.'];
}

/** Every problem of a parsed init config, logo file checks excluded (empty when valid). */
export function validateInitConfig(config) {
  if (!isObject(config)) return ['The init config must be a JSON object.'];
  const errors = [];
  const missing = INIT_CONFIG_FIELDS.filter((field) => !(field in config));
  const unknown = Object.keys(config).filter((field) => !INIT_CONFIG_FIELDS.includes(field));
  if (missing.length > 0) errors.push(`Missing field(s): ${missing.join(', ')}.`);
  if (unknown.length > 0) errors.push(`Unknown field(s): ${unknown.join(', ')}.`);
  if (missing.length > 0) return errors;
  const refusals = [
    refusalForPrefixFormat(config.prefix),
    refusalForName('nameEn', config.nameEn),
    refusalForName('nameAr', config.nameAr),
    refusalForColor(config.primaryColor),
    config.logoPath === null || (typeof config.logoPath === 'string' && config.logoPath.length > 0) ? null : 'logoPath must be a path or null.',
    refusalForLoginModes(config.loginModes),
    refusalForOptionalModules(config.optionalModules),
  ];
  errors.push(...refusals.filter((refusal) => refusal !== null), ...databaseErrors(config.database));
  return errors;
}

/**
 * The canonical form of a valid config: trimmed names, lower-case colour, logo resolved against `baseDir`, login
 * modes in LOGIN_MODES order.
 */
export function normalizeInitConfig(config, baseDir) {
  return {
    prefix: config.prefix,
    nameEn: config.nameEn.trim(),
    nameAr: config.nameAr.trim(),
    primaryColor: config.primaryColor.toLowerCase(),
    logoPath: config.logoPath === null ? null : path.resolve(baseDir, config.logoPath),
    loginModes: LOGIN_MODES.filter((mode) => config.loginModes.includes(mode)),
    optionalModules: Object.fromEntries(OPTIONAL_MODULE_IDS.map((id) => [id, config.optionalModules[id]])),
    database: { connectionString: config.database.connectionString?.trim() ?? null, skip: config.database.skip },
  };
}

/** Validates (logo included) and normalizes; returns { config } or { errors }. */
export function checkInitConfig(raw, baseDir) {
  const errors = validateInitConfig(raw);
  if (errors.length > 0) return { errors };
  const config = normalizeInitConfig(raw, baseDir);
  const logoRefusal = refusalForLogo(config.logoPath);
  return logoRefusal ? { errors: [logoRefusal] } : { config };
}

/**
 * Reads a -ConfigFile; a relative logoPath is resolved against the file's folder.
 * @returns {{ config?: object, errors?: string[] }}
 */
export function readInitConfigFile(file) {
  if (!existsSync(file)) return { errors: [`Config file ${file} not found.`] };
  let raw;
  try {
    raw = JSON.parse(readFileSync(file, 'utf8').replace(UTF8_BOM, ''));
  } catch (error) {
    return { errors: [`Config file ${file} is not valid JSON: ${error.message}`] };
  }
  return checkInitConfig(raw, path.dirname(file));
}

/** The optional module ids the config leaves unticked (to remove), in OPTIONAL_MODULE_IDS order. */
export function untickedModules(config) {
  return OPTIONAL_MODULE_IDS.filter((id) => !config.optionalModules[id]);
}

/** The secret-owning services New-LocalSecrets.ps1 should skip (unticked ones). */
export function skippedSecretServices(config) {
  return SECRET_OWNING_MODULE_IDS.filter((id) => !config.optionalModules[id]);
}
