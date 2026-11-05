// Feed mode for a fork (ADR-002): which Codelab package versions the feed holds, and the edits that make the build
// and npm resolve Codelab.* / @codelab/* only from the fork's own feed\ folder. Pure functions over text and names.
import {
  BUILD_SDK_ID,
  BUILD_SDK_VERSION_PROPERTY,
  CI_PRERELEASE_LABEL,
  FEED_NPM_DIR,
  FEED_NUGET_DIR,
  FEED_PIN_ITEM_GROUP_LABEL,
  NPM_DEPENDENCY_FIELDS,
  NPM_EXTENSION,
  NPM_FILE_SPEC_PREFIX,
  NPM_SCOPE,
  NPM_SCOPE_FILE_PREFIX,
  NUGET_EXTENSION,
  PACKAGE_SOURCE_SWITCH,
  PACKAGE_WORKSPACE_PREFIX,
} from './constants.mjs';
import { setAtPath } from './prefix.mjs';

/** Package kinds as packages/versions.json implies them (an id starting with '@' is npm). */
export const PackageKind = Object.freeze({ nuget: 'nuget', npm: 'npm' });

const REGEX_SPECIAL = /[.*+?^${}()|[\]\\]/g;
const INDENT_PROBE = /^[{[]\r?\n([ \t]+)\S/;
const PROJECT_CLOSE = /([ \t]*)<\/Project>\s*$/;
const LF = '\n';
const CRLF = '\r\n';
const DEFAULT_XML_INDENT = '  ';

const escapeRegex = (value) => value.replace(REGEX_SPECIAL, '\\$&');

export function kindOf(id) {
  return id.startsWith('@') ? PackageKind.npm : PackageKind.nuget;
}

/** The feed file name of a package version: Codelab.X.1.0.0.nupkg, codelab-ng-x-1.0.0.tgz. */
export function feedFileName(id, version) {
  if (kindOf(id) === PackageKind.nuget) return `${id}.${version}${NUGET_EXTENSION}`;
  return `${NPM_SCOPE_FILE_PREFIX}${id.slice(NPM_SCOPE.length)}-${version}${NPM_EXTENSION}`;
}

/** The feed sub-folder of a package kind. */
export function feedFolderOf(id) {
  return kindOf(id) === PackageKind.nuget ? FEED_NUGET_DIR : FEED_NPM_DIR;
}

/**
 * The version of `id` to pin from the feed's file names: the exact `baseVersion` when present, else its highest
 * `<baseVersion>-ci.<n>` prerelease, else null. NuGet names compare case-insensitively.
 * @param {string} id
 * @param {string} baseVersion plain x.y.z from packages/versions.json
 * @param {string[]} fileNames names in the feed sub-folder
 */
export function resolveFeedVersion(id, baseVersion, fileNames) {
  const ignoreCase = kindOf(id) === PackageKind.nuget;
  const names = new Set(fileNames.map((name) => (ignoreCase ? name.toLowerCase() : name)));
  const key = (name) => (ignoreCase ? name.toLowerCase() : name);
  if (names.has(key(feedFileName(id, baseVersion)))) return baseVersion;
  const template = feedFileName(id, `${baseVersion}-${CI_PRERELEASE_LABEL}.@@`);
  const [head, tail] = template.split('@@').map(escapeRegex);
  const pattern = new RegExp(`^${head}(\\d+)${tail}$`, ignoreCase ? 'i' : '');
  const numbers = fileNames.map((name) => pattern.exec(name)?.[1]).filter(Boolean).map(Number);
  if (numbers.length === 0) return null;
  return `${baseVersion}-${CI_PRERELEASE_LABEL}.${Math.max(...numbers)}`;
}

/**
 * Resolves every package of versions.json against the feed listing.
 * @param {Record<string, string>} versions packages/versions.json
 * @param {{ nuget: string[], npm: string[] }} listing file names per feed sub-folder
 * @returns {{ resolved: { id: string, version: string, file: string, folder: string }[], missing: string[] }}
 */
export function resolveFeedPackages(versions, listing) {
  const resolved = [];
  const missing = [];
  for (const [id, baseVersion] of Object.entries(versions)) {
    const folder = feedFolderOf(id);
    const version = resolveFeedVersion(id, baseVersion, listing[folder] ?? []);
    if (version === null) missing.push(`${id} ${baseVersion}`);
    else resolved.push({ id, version, file: feedFileName(id, version), folder });
  }
  return { resolved, missing };
}

function replaceElementText(text, element, replacement, description) {
  const pattern = new RegExp(`(<${element}\\b[^>]*>)([^<]*)(</${element}>)`);
  if (!pattern.test(text)) throw new Error(`${description}: no <${element}> element found.`);
  return text.replace(pattern, (_match, open, _value, close) => `${open}${replacement}${close}`);
}

/**
 * Directory.Build.props in feed mode: UseCodelabPackageSource defaults to false and the SDK pin is the feed version.
 * Throws when either element is missing (the file is not the starter's).
 */
export function setFeedModeBuildProps(text, sdkVersion) {
  const switched = replaceElementText(text, PACKAGE_SOURCE_SWITCH, 'false', 'Directory.Build.props');
  return replaceElementText(switched, BUILD_SDK_VERSION_PROPERTY, sdkVersion, 'Directory.Build.props');
}

function newlineOf(text) {
  return text.includes(CRLF) ? CRLF : LF;
}

function pinExisting(text, id, version) {
  const element = new RegExp(`(<PackageVersion\\b[^>]*\\bInclude="${escapeRegex(id)}"[^>]*\\bVersion=")[^"]*(")`, 'i');
  const reversed = new RegExp(`(<PackageVersion\\b[^>]*\\bVersion=")[^"]*("[^>]*\\bInclude="${escapeRegex(id)}")`, 'i');
  if (element.test(text)) return text.replace(element, `$1${version}$2`);
  if (reversed.test(text)) return text.replace(reversed, `$1${version}$2`);
  return null;
}

/**
 * Directory.Packages.props with every Codelab NuGet package pinned (Codelab.Build.Sdk excluded: it is an MSBuild
 * SDK, pinned in Directory.Build.props). Existing PackageVersion entries get the version; the others are added in
 * one labelled ItemGroup before </Project>.
 * @param {string} text
 * @param {Record<string, string>} pins id -> version
 */
export function pinPackageVersions(text, pins) {
  let result = text;
  const added = [];
  for (const [id, version] of Object.entries(pins)) {
    if (id === BUILD_SDK_ID || kindOf(id) !== PackageKind.nuget) continue;
    const pinned = pinExisting(result, id, version);
    if (pinned === null) added.push([id, version]);
    else result = pinned;
  }
  if (added.length === 0) return result;
  const close = PROJECT_CLOSE.exec(result);
  if (!close) throw new Error('Directory.Packages.props: no </Project> element found.');
  const newline = newlineOf(result);
  const indent = DEFAULT_XML_INDENT;
  const items = added.map(([id, version]) => `${indent}${indent}<PackageVersion Include="${id}" Version="${version}" />`);
  const group = [`${indent}<ItemGroup Label="${FEED_PIN_ITEM_GROUP_LABEL}">`, ...items, `${indent}</ItemGroup>`, ''].join(newline);
  return result.slice(0, close.index) + newline + group + result.slice(close.index);
}

/**
 * An npm manifest in feed mode: every @codelab/* dependency becomes file:<relativeFeed>/<tarball>, and (for the
 * workspace root) the packages/* workspace globs are dropped. Keeps indentation, line endings and final newline.
 * @param {string} text package.json content
 * @param {Record<string, string>} tarballs @codelab id -> tarball file name
 * @param {string} relativeNpmFeed '/'-separated path from the manifest's folder to the fork's feed/npm
 * @returns {{ text: string, rewritten: string[], droppedWorkspaces: string[] }}
 */
export function rewriteNpmManifest(text, tarballs, relativeNpmFeed) {
  const manifest = JSON.parse(text);
  const rewritten = [];
  for (const field of NPM_DEPENDENCY_FIELDS) {
    for (const name of Object.keys(manifest[field] ?? {})) {
      if (!name.startsWith(NPM_SCOPE)) continue;
      if (!tarballs[name]) throw new Error(`${name} is not in packages/versions.json or not in the feed.`);
      setAtPath(manifest, [field, name], `${NPM_FILE_SPEC_PREFIX}${relativeNpmFeed}/${tarballs[name]}`);
      rewritten.push(name);
    }
  }
  const droppedWorkspaces = [];
  if (Array.isArray(manifest.workspaces)) {
    droppedWorkspaces.push(...manifest.workspaces.filter((glob) => glob.startsWith(PACKAGE_WORKSPACE_PREFIX)));
    manifest.workspaces = manifest.workspaces.filter((glob) => !glob.startsWith(PACKAGE_WORKSPACE_PREFIX));
  }
  const indent = INDENT_PROBE.exec(text)?.[1] ?? DEFAULT_XML_INDENT;
  const newline = newlineOf(text);
  const body = JSON.stringify(manifest, null, indent).split(LF).join(newline);
  return { text: body + (text.endsWith(LF) ? newline : ''), rewritten, droppedWorkspaces };
}
