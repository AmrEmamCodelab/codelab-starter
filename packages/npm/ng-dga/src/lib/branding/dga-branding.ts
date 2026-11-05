/**
 * The branding parameter layer: one project's primary colour, logo and bilingual name. A fork sets it once (`init.ps1`
 * writes it into the app's runtime configuration); {@link applyDgaBranding} turns the colour into the `--dga-brand-*`
 * custom properties that `styles/_branding.scss` maps onto the semantic primary tokens. The library itself carries no
 * text: the name arrives from the configuration in both languages.
 */
export interface DgaBranding {
  /** The brand primary colour as hex: `#RGB` or `#RRGGBB`. */
  readonly primaryColor: string;
  /** The logo image: a path served by the app (`branding/logo.svg`, `/branding/logo.png`) or empty for no logo. */
  readonly logoSrc: string;
  /** The product name in both languages. */
  readonly appName: DgaBrandingName;
}

export interface DgaBrandingName {
  readonly ar: string;
  readonly en: string;
}

/** The custom properties {@link applyDgaBranding} sets on the root element. */
export const DgaBrandProperty = {
  Primary: '--dga-brand-primary',
  PrimaryHover: '--dga-brand-primary-hover',
  PrimaryActive: '--dga-brand-primary-active',
  PrimarySubtle: '--dga-brand-primary-subtle',
  PrimaryMuted: '--dga-brand-primary-muted',
} as const;
export type DgaBrandProperty = (typeof DgaBrandProperty)[keyof typeof DgaBrandProperty];

/** The fields of a branding block, as named in a validation error. */
export const DgaBrandingField = {
  Root: '$',
  PrimaryColor: 'primaryColor',
  LogoSrc: 'logoSrc',
  AppName: 'appName',
  AppNameArabic: 'appName.ar',
  AppNameEnglish: 'appName.en',
} as const;
export type DgaBrandingField = (typeof DgaBrandingField)[keyof typeof DgaBrandingField];

/** Thrown when a branding block is missing a field or has a malformed one; `field` names it. */
export class DgaBrandingInvalidError extends Error {
  constructor(readonly field: DgaBrandingField) {
    super(`Branding is invalid: field "${field}" is missing or malformed.`);
    this.name = 'DgaBrandingInvalidError';
  }
}

const HEX_COLOR = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i;
/** A URL scheme (`http:`, `javascript:`) or a protocol-relative `//host`: the logo must be served by the app itself. */
const EXTERNAL_OR_SCHEME = /^(?:[a-z][a-z0-9+.-]*:|\/\/|\\\\)/i;
/**
 * A backslash, raw or percent-encoded: browsers read it as a slash in http(s) URLs, so `/\host` or `\/host` would
 * reach a remote host. A logo path served by the app never needs one.
 */
const BACKSLASH = /\\|%5c/i;
/** Whitespace, quotes, parentheses or angle brackets: never part of a plain served path. */
const UNSAFE_PATH_CHARACTER = /[\s"'()<>]/;
/**
 * A stand-in origin the logo path is resolved against: a path the app serves itself resolves to this same origin,
 * anything that reaches another host does not. The `.invalid` top-level domain is reserved and never resolves.
 */
const PROBE_ORIGIN = 'https://app.invalid';
const PROBE_BASE = `${PROBE_ORIGIN}/`;
const MAX_NAME_LENGTH = 120;
const MAX_LOGO_SRC_LENGTH = 512;

/** Shades derived from the primary colour with color-mix, as the percentage of the primary colour kept. */
const HOVER_PRIMARY_PERCENT = 85;
const ACTIVE_PRIMARY_PERCENT = 70;
const SUBTLE_PRIMARY_PERCENT = 8;
const MUTED_PRIMARY_PERCENT = 18;
const DARKEN_WITH = 'black';
const LIGHTEN_WITH = 'white';

/** True for a `#RGB` or `#RRGGBB` hex colour. */
export function isDgaHexColor(value: unknown): value is string {
  return typeof value === 'string' && HEX_COLOR.test(value);
}

/**
 * True for an empty logo (none) or a path the app serves itself: no scheme, no remote host (also not through a
 * backslash, which browsers read as a slash), no quotes or spaces, and it resolves to the app's own origin.
 */
export function isDgaLogoSrc(value: unknown): value is string {
  if (typeof value !== 'string') return false;
  if (value === '') return true;
  if (value.length > MAX_LOGO_SRC_LENGTH) return false;
  if (EXTERNAL_OR_SCHEME.test(value) || BACKSLASH.test(value) || UNSAFE_PATH_CHARACTER.test(value)) return false;
  return resolvesToTheApp(value);
}

/** True when the path, resolved the way a browser resolves it, stays on the app's own origin. */
function resolvesToTheApp(path: string): boolean {
  try {
    return new URL(path, PROBE_BASE).origin === PROBE_ORIGIN;
  } catch {
    return false;
  }
}

function isName(value: unknown): value is string {
  return typeof value === 'string' && value.trim() !== '' && value.length <= MAX_NAME_LENGTH;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function required<T>(value: unknown, field: DgaBrandingField, check: (candidate: unknown) => candidate is T): T {
  if (!check(value)) throw new DgaBrandingInvalidError(field);
  return value;
}

/**
 * Validates an untyped branding block (from `config.json`) and returns a typed copy with the colour in lower case.
 *
 * @throws DgaBrandingInvalidError naming the first bad field.
 */
export function parseDgaBranding(raw: unknown): DgaBranding {
  const root = required(raw, DgaBrandingField.Root, isObject);
  const appName = required(root['appName'], DgaBrandingField.AppName, isObject);
  return {
    primaryColor: required(root['primaryColor'], DgaBrandingField.PrimaryColor, isDgaHexColor).toLowerCase(),
    logoSrc: required(root['logoSrc'], DgaBrandingField.LogoSrc, isDgaLogoSrc),
    appName: {
      ar: required(appName['ar'], DgaBrandingField.AppNameArabic, isName),
      en: required(appName['en'], DgaBrandingField.AppNameEnglish, isName),
    },
  };
}

function mix(color: string, percent: number, other: string): string {
  return `color-mix(in srgb, ${color} ${percent}%, ${other})`;
}

/** The `--dga-brand-*` values for a primary colour: the colour itself and its derived hover, active and tint shades. */
export function dgaBrandProperties(primaryColor: string): Readonly<Record<DgaBrandProperty, string>> {
  const color = required(primaryColor, DgaBrandingField.PrimaryColor, isDgaHexColor).toLowerCase();
  return {
    [DgaBrandProperty.Primary]: color,
    [DgaBrandProperty.PrimaryHover]: mix(color, HOVER_PRIMARY_PERCENT, DARKEN_WITH),
    [DgaBrandProperty.PrimaryActive]: mix(color, ACTIVE_PRIMARY_PERCENT, DARKEN_WITH),
    [DgaBrandProperty.PrimarySubtle]: mix(color, SUBTLE_PRIMARY_PERCENT, LIGHTEN_WITH),
    [DgaBrandProperty.PrimaryMuted]: mix(color, MUTED_PRIMARY_PERCENT, LIGHTEN_WITH),
  };
}

/**
 * Applies a branding to a document: validates it, then sets the `--dga-brand-*` custom properties on the root
 * element (`:root`), which the package styles map onto `--dga-color-primary*`, the primary border and text, the
 * progress colour and the focus ring. Calling it again replaces the previous values.
 *
 * @throws DgaBrandingInvalidError when the branding is malformed (nothing is changed then).
 */
export function applyDgaBranding(doc: Document, branding: DgaBranding): void {
  const valid = parseDgaBranding(branding);
  const style = doc.documentElement.style;
  for (const [property, value] of Object.entries(dgaBrandProperties(valid.primaryColor))) {
    style.setProperty(property, value);
  }
}
