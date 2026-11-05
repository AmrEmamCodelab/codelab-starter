// Pure helpers of the fork-model test (tools/fork/Test-ForkModel.ps1): probe edits, the scripted conflict
// resolution, and the checks that Codelab packages came only from the fork's feed.
import path from 'node:path';
import { NPM_FILE_SPEC_PREFIX, NPM_SCOPE } from './constants.mjs';

/** Comment syntax per file extension for the probe line appended to an edited file. */
const COMMENT_STYLES = Object.freeze({
  '.md': ['<!-- ', ' -->'],
  '.html': ['<!-- ', ' -->'],
  '.cs': ['// ', ''],
  '.ts': ['// ', ''],
  '.mjs': ['// ', ''],
  '.js': ['// ', ''],
  '.scss': ['// ', ''],
  '.ps1': ['# ', ''],
  '.yaml': ['# ', ''],
  '.yml': ['# ', ''],
  '.txt': ['', ''],
});
const CONFLICT_BLOCK = /^<{7}[^\n]*\n([\s\S]*?)(?:^\|{7}[^\n]*\n[\s\S]*?)?^={7}\r?\n([\s\S]*?)^>{7}[^\n]*(?:\n|$)/gm;
const NUGET_METADATA_SOURCE = 'source';
const ASSETS_LIBRARY_SEPARATOR = '/';
const PACKAGE_LIBRARY_TYPE = 'package';
const CODELAB_ID_PREFIX = 'codelab.';
const NODE_MODULES_SCOPE = `node_modules/${NPM_SCOPE}`;
const TRAILING_SEPARATORS = /[\\/]+$/;

/** True when the extension has a probe comment style. */
export function canProbe(file) {
  return Object.hasOwn(COMMENT_STYLES, path.extname(file).toLowerCase());
}

/** Appends one probe line (as a comment in the file's language) at the end of the text, keeping its line endings. */
export function appendProbe(text, file, label) {
  const style = COMMENT_STYLES[path.extname(file).toLowerCase()];
  if (!style) throw new Error(`${file}: no probe comment style for this extension.`);
  const newline = text.includes('\r\n') ? '\r\n' : '\n';
  const body = text.length === 0 || text.endsWith('\n') ? text : `${text}${newline}`;
  return `${body}${style[0]}fork-model probe: ${label}${style[1]}${newline}`;
}

/** Resolves every conflict block by keeping both sides, ours first (the scripted resolution of the test). */
export function resolveByUnion(text) {
  return text.replace(CONFLICT_BLOCK, (_block, ours, theirs) => `${ours}${theirs}`);
}

/** The difference between two path sets, for an assertion message. */
export function compareSets(expected, actual) {
  const want = new Set(expected);
  const got = new Set(actual);
  return { missing: [...want].filter((item) => !got.has(item)), unexpected: [...got].filter((item) => !want.has(item)) };
}

function samePath(left, right) {
  const normalize = (value) => path.resolve(value).replace(TRAILING_SEPARATORS, '').toLowerCase();
  return normalize(left) === normalize(right);
}

/**
 * Checks one project.assets.json: Codelab libraries are packages (never projects) and the fork's feed is a restore
 * source. Returns failure messages.
 * @param {object} assets parsed project.assets.json
 * @param {string} feedNuGet absolute path of the fork's feed\nuget
 * @param {string} label for messages
 */
export function checkAssets(assets, feedNuGet, label) {
  const failures = [];
  const codelab = Object.entries(assets.libraries ?? {}).filter(([key]) => key.toLowerCase().startsWith(CODELAB_ID_PREFIX));
  for (const [key, library] of codelab) {
    if (library.type !== PACKAGE_LIBRARY_TYPE) failures.push(`${label}: ${key.split(ASSETS_LIBRARY_SEPARATOR)[0]} is a ${library.type}, not a feed package.`);
  }
  const sources = Object.keys(assets.project?.restore?.sources ?? {});
  if (codelab.length > 0 && !sources.some((source) => samePath(source, feedNuGet))) {
    failures.push(`${label}: the fork feed ${feedNuGet} is not a restore source (${sources.join(', ')}).`);
  }
  return failures;
}

/**
 * Checks the .nupkg.metadata of every restored Codelab package: its source must be the fork's feed.
 * @param {{ id: string, version: string, metadata: object }[]} restored
 * @param {string} feedNuGet
 */
export function checkNuGetOrigins(restored, feedNuGet) {
  return restored
    .filter((entry) => !samePath(entry.metadata[NUGET_METADATA_SOURCE] ?? '', feedNuGet))
    .map((entry) => `${entry.id} ${entry.version} came from ${entry.metadata[NUGET_METADATA_SOURCE]}, not ${feedNuGet}.`);
}

/** True for a restored package folder name that is a Codelab package. */
export function isCodelabPackageFolder(name) {
  return name.toLowerCase().startsWith(CODELAB_ID_PREFIX);
}

/**
 * Checks package-lock.json: every installed @codelab/* package resolves to a tarball in the fork's feed/npm (never
 * a workspace link or a registry).
 * @param {object} lock parsed package-lock.json (v3)
 * @param {string} npmFeedRelative '/'-separated feed/npm path relative to the lock's folder
 */
export function checkNpmLock(lock, npmFeedRelative) {
  const failures = [];
  for (const [key, entry] of Object.entries(lock.packages ?? {})) {
    if (!key.includes(NODE_MODULES_SCOPE)) continue;
    const resolved = entry.resolved ?? '';
    const fromFeed = resolved.startsWith(NPM_FILE_SPEC_PREFIX) && resolved.slice(NPM_FILE_SPEC_PREFIX.length).replace(/^\.\//, '').startsWith(`${npmFeedRelative}/`);
    if (entry.link || !fromFeed) failures.push(`${key} resolves to '${resolved || '(link)'}', not a tarball in ${npmFeedRelative}.`);
  }
  return failures;
}
