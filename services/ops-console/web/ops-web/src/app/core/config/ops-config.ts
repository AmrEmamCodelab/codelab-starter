import { InjectionToken } from '@angular/core';
import { isAppLanguage, type AppLanguage } from '../i18n/language';

/** Where the runtime configuration is served, relative to the base href. */
export const RUNTIME_CONFIG_PATH = 'config.json';

/**
 * The console's runtime configuration (`public/config.json`; each deployment overwrites it, one build serves every
 * environment).
 *
 * - `basePath`: the path the app is mounted at (`/ops/` for a child app under the existing site). It must equal the
 *   `<base href>` of the served `index.html`; the installer writes the base href from this value
 *   (`tools/apply-base-href.mjs`), and start-up refuses a mismatch (`BasePathMismatchError`).
 * - `apiBaseUrl`: the BFF root on the SAME origin (`/ops-api`). Absolute URLs and protocol-relative `//host` values are
 *   refused: the console sends Windows credentials and must never send them elsewhere.
 * - `defaultLanguage`: the first-visit language (`ar` or `en`).
 * - `idpCertificateName`: the `name` of the identity provider's signing certificate in `/ops-api/certificates`, or
 *   null. The certificate alert banner follows this certificate when it is set and every certificate otherwise.
 */
export interface OpsConfig {
  readonly basePath: string;
  readonly apiBaseUrl: string;
  readonly defaultLanguage: AppLanguage;
  readonly idpCertificateName: string | null;
}

/** The config keys, as written in config.json. */
export const OpsConfigKeys = {
  BasePath: 'basePath',
  ApiBaseUrl: 'apiBaseUrl',
  DefaultLanguage: 'defaultLanguage',
  IdpCertificateName: 'idpCertificateName',
} as const;

/** config.json is not an object, or a key is missing or malformed. The message names the key, never its value. */
export class OpsConfigInvalidError extends Error {
  constructor(readonly key: string) {
    super(`config.json: "${key}" is missing or invalid.`);
    this.name = 'OpsConfigInvalidError';
  }
}

const PATH_SEPARATOR = '/';
const PROTOCOL_RELATIVE_PREFIX = '//';
const PARENT_SEGMENT = '..';
const BACKSLASH = '\\';
const TRAILING_SEPARATORS = /\/+$/;

/** A site-relative path: starts with one `/`, never `//` (another host), no backslash and no `..` segment. */
function isSiteRelativePath(value: unknown): value is string {
  return typeof value === 'string'
    && value.startsWith(PATH_SEPARATOR)
    && !value.startsWith(PROTOCOL_RELATIVE_PREFIX)
    && !value.includes(BACKSLASH)
    && !value.split(PATH_SEPARATOR).includes(PARENT_SEGMENT);
}

function parseBasePath(value: unknown): string {
  if (!isSiteRelativePath(value) || !value.endsWith(PATH_SEPARATOR)) {
    throw new OpsConfigInvalidError(OpsConfigKeys.BasePath);
  }
  return value;
}

/** The BFF root without trailing slashes (`/ops-api/` becomes `/ops-api`); the site root itself is refused. */
function parseApiBaseUrl(value: unknown): string {
  if (!isSiteRelativePath(value)) {
    throw new OpsConfigInvalidError(OpsConfigKeys.ApiBaseUrl);
  }
  const trimmed = value.replace(TRAILING_SEPARATORS, '');
  if (trimmed === '') {
    throw new OpsConfigInvalidError(OpsConfigKeys.ApiBaseUrl);
  }
  return trimmed;
}

function parseIdpCertificateName(value: unknown): string | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value !== 'string' || value.trim() === '') {
    throw new OpsConfigInvalidError(OpsConfigKeys.IdpCertificateName);
  }
  return value;
}

/**
 * Validates config.json. Fails loudly: a console that silently talks to the wrong root is worse than one that refuses
 * to start.
 *
 * @throws OpsConfigInvalidError naming the first bad key.
 */
export function parseOpsConfig(raw: unknown): OpsConfig {
  if (raw === null || typeof raw !== 'object' || Array.isArray(raw)) {
    throw new OpsConfigInvalidError(RUNTIME_CONFIG_PATH);
  }
  const record = raw as Record<string, unknown>;
  const defaultLanguage = record[OpsConfigKeys.DefaultLanguage];
  if (!isAppLanguage(defaultLanguage)) {
    throw new OpsConfigInvalidError(OpsConfigKeys.DefaultLanguage);
  }
  return {
    basePath: parseBasePath(record[OpsConfigKeys.BasePath]),
    apiBaseUrl: parseApiBaseUrl(record[OpsConfigKeys.ApiBaseUrl]),
    defaultLanguage,
    idpCertificateName: parseIdpCertificateName(record[OpsConfigKeys.IdpCertificateName]),
  };
}

/** The validated runtime config, provided once at bootstrap (`main.ts`). */
export const OPS_CONFIG = new InjectionToken<OpsConfig>('OPS_CONFIG');
