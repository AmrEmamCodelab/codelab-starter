import { type DgaBranding, DgaBrandingField, DgaBrandingInvalidError, parseDgaBranding } from '@codelab/ng-dga';
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
  /**
   * Seconds between background permission syncs while signed in, so a grant or revoke shows without a new login
   * (0 turns the interval off; focus and navigation still sync). Optional in the file: defaults to
   * {@link DEFAULT_PERMISSION_REFRESH_SECONDS}.
   */
  readonly permissionRefreshSeconds: number;
  /**
   * The project's branding (primary colour, logo, name in ar and en), written by `init.ps1`. Optional: without it
   * the DGA default green applies. Validated by `@codelab/ng-dga` (hex colour, app-served logo path, both names).
   */
  readonly branding?: DgaBranding;
  /**
   * Serves the live component showcase at `/showcase` (dev and staging only). Optional in the file: defaults to
   * false, so a production config that names nothing never exposes it. A development build forces it on
   * (`DEVELOPMENT_BUILD_OVERRIDES` in runtime-config.loader.ts).
   */
  readonly showcase: boolean;
  /**
   * Offers the public contact form at `/portal/contact`. Optional in the file: defaults to false until the deployed
   * API serves `POST api/portal/contact-messages`, so the page never shows a form that can only fail; while off the
   * page shows the ways to reach the team and a "form coming soon" notice.
   */
  readonly portalContactForm: boolean;
}

/** The background permission sync interval when `config.json` names none. */
export const DEFAULT_PERMISSION_REFRESH_SECONDS = 30;

/** The longest accepted interval (one day): a larger value is a typo, not a policy. */
export const MAX_PERMISSION_REFRESH_SECONDS = 86_400;

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

/** The config.json field of the branding block; a bad branding field is reported as `branding.<field>`. */
export const BRANDING_FIELD = 'branding';

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

function isRefreshSeconds(value: unknown): value is number {
  return Number.isInteger(value) && (value as number) >= 0 && (value as number) <= MAX_PERMISSION_REFRESH_SECONDS;
}

function isBoolean(value: unknown): value is boolean {
  return typeof value === 'boolean';
}

/** The branding block, validated by its owner (`parseDgaBranding`); undefined when the file has none. */
function optionalBranding(value: unknown): DgaBranding | undefined {
  if (value === undefined) return undefined;
  try {
    return parseDgaBranding(value);
  } catch (error) {
    if (error instanceof DgaBrandingInvalidError) {
      const isRoot = error.field === DgaBrandingField.Root;
      throw new AppConfigInvalidError(isRoot ? BRANDING_FIELD : `${BRANDING_FIELD}.${error.field}`);
    }
    throw error;
  }
}

function optionalField<T>(value: unknown, field: string, check: (candidate: unknown) => candidate is T, fallback: T): T {
  return value === undefined ? fallback : requireField(value, field, check);
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
    permissionRefreshSeconds: optionalField(
      root['permissionRefreshSeconds'],
      'permissionRefreshSeconds',
      isRefreshSeconds,
      DEFAULT_PERMISSION_REFRESH_SECONDS,
    ),
    ...withBranding(optionalBranding(root[BRANDING_FIELD])),
    showcase: optionalField(root['showcase'], 'showcase', isBoolean, false),
    portalContactForm: optionalField(root['portalContactForm'], 'portalContactForm', isBoolean, false),
  };
}

/** Leaves the key out entirely when the file has no branding, so a parsed config compares equal to its source. */
function withBranding(branding: DgaBranding | undefined): Pick<AppConfig, 'branding'> {
  return branding === undefined ? {} : { branding };
}

/** Joins a path onto a base, collapsing the slashes on either side so a leading slash never matters. */
export function joinUrl(base: string, path: string): string {
  const trimmedBase = base.replace(/\/+$/, '');
  const trimmedPath = path.replace(/^\/+/, '');
  return trimmedPath === '' ? trimmedBase : `${trimmedBase}/${trimmedPath}`;
}
