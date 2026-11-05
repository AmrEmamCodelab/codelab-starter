import { isAppLanguage, type AppLanguage } from '../i18n/language';

/**
 * The runtime configuration served as `public/config.json` (strict JSON: a fork's init rewrites it).
 * One build ships to every environment; the deployment writes this file next to `index.html`.
 * The file is public, so it carries only what the browser cannot start without.
 */
export interface AppConfig {
  /** The API root every `ApiService` path resolves against (absolute origin or same-origin prefix). */
  readonly apiBaseUrl: string;
  /** The roots whose calls are signed and verified (`api`), and attachment roots (none yet). */
  readonly origins: AppConfigOrigins;
  readonly security: AppConfigSecurity;
  /** The language a first visit renders in. */
  readonly defaultLanguage: AppLanguage;
}

export interface AppConfigOrigins {
  readonly api: readonly string[];
  readonly attachment: readonly string[];
}

export interface AppConfigSecurity {
  /** The request-signing protocol prefix; must equal the API's `Codelab:Security:Protocol:Prefix`. */
  readonly protocolPrefix: string;
}

/** Served from `public/`, so it lands at the web root. */
export const RUNTIME_CONFIG_PATH = '/config.json';

/** Thrown at startup when `config.json` is missing a field or has the wrong shape. */
export class AppConfigInvalidError extends Error {
  constructor(readonly field: string) {
    super(`Runtime configuration ${RUNTIME_CONFIG_PATH} is invalid: field "${field}" is missing or malformed.`);
    this.name = 'AppConfigInvalidError';
  }
}

type JsonObject = Record<string, unknown>;

function isObject(value: unknown): value is JsonObject {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== '';
}

function isStringList(value: unknown): value is string[] {
  return Array.isArray(value) && value.every(isNonEmptyString);
}

function requireField<T>(value: unknown, field: string, check: (candidate: unknown) => candidate is T): T {
  if (!check(value)) {
    throw new AppConfigInvalidError(field);
  }
  return value;
}

/**
 * Validates the parsed `config.json` and returns a typed copy.
 *
 * @throws AppConfigInvalidError naming the first bad field; the app refuses to start rather than talk to
 * the wrong API.
 */
export function parseAppConfig(raw: unknown): AppConfig {
  const root = requireField(raw, '$', isObject);
  const origins = requireField(root['origins'], 'origins', isObject);
  const security = requireField(root['security'], 'security', isObject);
  return {
    apiBaseUrl: requireField(root['apiBaseUrl'], 'apiBaseUrl', isNonEmptyString),
    origins: {
      api: requireField(origins['api'], 'origins.api', isStringList),
      attachment: requireField(origins['attachment'], 'origins.attachment', isStringList),
    },
    security: {
      protocolPrefix: requireField(security['protocolPrefix'], 'security.protocolPrefix', isNonEmptyString),
    },
    defaultLanguage: requireField(root['defaultLanguage'], 'defaultLanguage', isAppLanguage),
  };
}

/** Joins a path onto a base, collapsing the slashes on either side so a leading slash never matters. */
export function joinUrl(base: string, path: string): string {
  const trimmedBase = base.replace(/\/+$/, '');
  const trimmedPath = path.replace(/^\/+/, '');
  return trimmedPath === '' ? trimmedBase : `${trimmedBase}/${trimmedPath}`;
}
