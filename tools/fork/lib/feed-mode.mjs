// init step e: switch a fork to feed mode. Pins the Codelab versions the feed holds, points npm at the fork's own
// feed tarballs and copies the needed packages into the fork's feed\ (its contents are gitignored).
import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import {
  BUILD_PROPS_FILE,
  BUILD_SDK_ID,
  FEED_DIR,
  FEED_NPM_DIR,
  FEED_NUGET_DIR,
  PACKAGES_PROPS_FILE,
  VERSIONS_FILE,
} from './constants.mjs';
import { pinPackageVersions, resolveFeedPackages, rewriteNpmManifest, setFeedModeBuildProps } from './feed.mjs';
import { toPosix } from './modules.mjs';
import { gitList } from './process.mjs';
import { RefusalError } from './repo.mjs';

function listFolder(folder) {
  return existsSync(folder) ? readdirSync(folder) : [];
}

/** Reads packages/versions.json (the captured package versions); refuses when it is missing or invalid. */
export function readPackageVersions(root) {
  const file = path.join(root, VERSIONS_FILE);
  if (!existsSync(file)) throw new RefusalError(`${VERSIONS_FILE} not found; init needs the package versions to pin.`);
  try {
    return JSON.parse(readFileSync(file, 'utf8'));
  } catch (error) {
    throw new RefusalError(`${VERSIONS_FILE} is not valid JSON: ${error.message}`);
  }
}

/** Resolves every package against the source feed; refuses when one is missing (pack it first). */
export function resolveFromFeed(versions, feedDir) {
  const listing = { [FEED_NUGET_DIR]: listFolder(path.join(feedDir, FEED_NUGET_DIR)), [FEED_NPM_DIR]: listFolder(path.join(feedDir, FEED_NPM_DIR)) };
  const { resolved, missing } = resolveFeedPackages(versions, listing);
  if (missing.length > 0) {
    throw new RefusalError(`The feed ${feedDir} lacks: ${missing.join(', ')}. Pack them first (tools\\build.ps1 -Pack) or pass -FeedDir.`);
  }
  return resolved;
}

function rewriteFile(root, relativePath, transform) {
  const file = path.join(root, relativePath);
  writeFileSync(file, transform(readFileSync(file, 'utf8')));
}

function copyPackages(root, feedDir, resolved) {
  const copied = [];
  for (const entry of resolved) {
    const target = path.join(root, FEED_DIR, entry.folder);
    const source = path.join(feedDir, entry.folder, entry.file);
    mkdirSync(target, { recursive: true });
    if (path.resolve(source).toLowerCase() !== path.resolve(target, entry.file).toLowerCase()) {
      copyFileSync(source, path.join(target, entry.file));
    }
    copied.push(`${FEED_DIR}/${entry.folder}/${entry.file}`);
  }
  const tracked = gitList(root, ['ls-files', '-z', '--cached', '--others', '--exclude-standard', '--', `${FEED_DIR}/`]);
  const leaked = copied.filter((file) => tracked.includes(file));
  if (leaked.length > 0) throw new RefusalError(`The fork's .gitignore does not ignore the feed contents: ${leaked.join(', ')}.`);
  return copied;
}

function rewriteNpmManifests(root, manifests, resolved) {
  const tarballs = Object.fromEntries(resolved.filter((entry) => entry.folder === FEED_NPM_DIR).map((entry) => [entry.id, entry.file]));
  const changes = [];
  for (const manifest of manifests) {
    const relativeFeed = toPosix(path.relative(path.dirname(path.join(root, manifest)), path.join(root, FEED_DIR, FEED_NPM_DIR)));
    rewriteFile(root, manifest, (text) => {
      const result = rewriteNpmManifest(text, tarballs, relativeFeed);
      changes.push({ manifest, rewritten: result.rewritten, droppedWorkspaces: result.droppedWorkspaces });
      return result.text;
    });
  }
  return changes;
}

/**
 * Applies feed mode to the fork at `root`.
 * @param {string} root
 * @param {{ versions: Record<string, string>, feedDir: string, npmManifests: string[] }} options npmManifests exist
 * @param {{ log: (line: string) => void }} io
 */
export function applyFeedMode(root, options, io) {
  const resolved = resolveFromFeed(options.versions, options.feedDir);
  const pins = Object.fromEntries(resolved.map((entry) => [entry.id, entry.version]));
  rewriteFile(root, BUILD_PROPS_FILE, (text) => setFeedModeBuildProps(text, pins[BUILD_SDK_ID]));
  rewriteFile(root, PACKAGES_PROPS_FILE, (text) => pinPackageVersions(text, pins));
  for (const change of rewriteNpmManifests(root, options.npmManifests, resolved)) {
    io.log(`  ${change.manifest}: feed tarballs for ${change.rewritten.join(', ') || 'no @codelab dependency'}`
      + (change.droppedWorkspaces.length > 0 ? `; dropped workspaces ${change.droppedWorkspaces.join(', ')}` : ''));
  }
  for (const file of copyPackages(root, options.feedDir, resolved)) io.log(`  copied ${file}`);
  for (const entry of resolved) io.log(`  pinned ${entry.id} ${entry.version}`);
  return pins;
}
