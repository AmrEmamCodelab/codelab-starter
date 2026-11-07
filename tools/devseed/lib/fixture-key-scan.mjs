// Finds the starter's TEST-ONLY fixture key material (packages/vectors/fixture-keys, the golden vector keys) and the
// test-only protocol prefixes in a tree. Keys are matched by digest only: the denylist holds sha256(hexDecode(d)) for
// every digest d of tools/extract/lib/key-material.mjs digestsOf(value) (the Codelab.Security published-material
// scheme), so neither this code nor its digest file holds a key a reader could reuse.
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { extname, join, relative, sep } from 'node:path';
import { sha256Hex } from '../../extract/lib/hashing.mjs';
import { digestsOf } from '../../extract/lib/key-material.mjs';
import { candidateValues, normalizePemBody } from '../../extract/lib/secret-scanner.mjs';

/** Where the committed digests come from (the Codelab.Security published-material denylist of the fixture keys). */
export const PUBLISHED_FIXTURES_PATH = 'packages/dotnet/Codelab.Security/PublishedMaterial/published-material.fixtures.json';
/** The copy kept with this tool, so an init-ed project (which has no packages/) can still run the check. */
export const DIGESTS_PATH = 'tools/devseed/fixture-key-digests.json';
export const ENTRIES_PROPERTY = 'keyDigestHashes';

/** Why a line is a hit. */
export const HitKinds = Object.freeze({
  fixtureKey: 'fixture-key',
  fixtureDocument: 'fixture-document',
  testProtocolPrefix: 'test-protocol-prefix',
});

/**
 * The notice every fixture key file carries (tools/vectors/lib/fixture-keys.mjs TEST_ONLY_NOTICE starts with it), built
 * from parts so this file is not itself a hit.
 */
const FIXTURE_NOTICE = ['TEST-ONLY', 'starter fixture key'].join(' ');
/** The protocol prefixes only the package tests and golden vectors use; a configuration file must never carry one. */
const TEST_PROTOCOL_PREFIXES = Object.freeze(['CODELABTEST', 'ALTPFX']);
const CONFIG_FILE = /^appsettings(\..+)?\.json$/i;

/** Folders never scanned: build output, dependencies, VCS data. */
const SKIPPED_FOLDERS = new Set(['.git', 'node_modules', 'bin', 'obj', 'dist', '.angular', 'artifacts', 'TestResults', 'coverage']);
/** Files never read: key containers (repository rule: never read *.pfx or *.key) and binaries. */
const SKIPPED_EXTENSIONS = new Set(['.pfx', '.key', '.p12', '.png', '.jpg', '.jpeg', '.gif', '.ico', '.woff', '.woff2', '.ttf', '.eot', '.zip', '.dll', '.exe', '.pdb', '.nupkg', '.tgz', '.xlsx', '.docx', '.pdf']);
/** Files larger than this are not text a key is configured in. */
const MAX_FILE_BYTES = 4 * 1024 * 1024;
const NUL = '\0';
const HEX = 'hex';
const PEM_BLOCK = /-----BEGIN [A-Z0-9 ]+-----([\s\S]*?)-----END [A-Z0-9 ]+-----/g;
const ESCAPED_NEWLINE = /\\r\\n|\\n/g;
const LINE_BREAK = /\r?\n/;
const LF = '\n';
const FIRST_LINE = 1;

/** The denylist entry of one key digest (hex SHA-256): the SHA-256 of its 32 bytes. */
export function entryFor(keyDigestHex) {
  return sha256Hex(Buffer.from(keyDigestHex, HEX));
}

/** True when `value` is one of the fixture keys (in any spelling digestsOf covers). */
export function isFixtureValue(value, entries) {
  return digestsOf(value).some((digest) => entries.has(entryFor(digest)));
}

/** The digest entries in a published-material style file ({ keyDigestHashes: [...] }). */
export function readEntries(file) {
  const entries = JSON.parse(readFileSync(file, 'utf8'))[ENTRIES_PROPERTY];
  if (!Array.isArray(entries) || entries.length === 0) throw new Error(`${file}: no ${ENTRIES_PROPERTY}`);
  return new Set(entries);
}

/** The entries to check against: an explicit file, else the published file of the repo, else the tool's copy. */
export function loadEntries(repoRoot, explicitFile) {
  if (explicitFile) return readEntries(explicitFile);
  const published = join(repoRoot, PUBLISHED_FIXTURES_PATH);
  return readEntries(existsSync(published) ? published : join(repoRoot, DIGESTS_PATH));
}

function lineOf(text, index) {
  return text.slice(0, index).split(LF).length;
}

function pemHits(text, entries) {
  const hits = [];
  // A PEM may be written with real line breaks or JSON-escaped ones (\n inside a string value).
  for (const variant of new Set([text, text.replace(ESCAPED_NEWLINE, LF)])) {
    for (const match of variant.matchAll(PEM_BLOCK)) {
      if (isFixtureValue(normalizePemBody(match[1]), entries)) hits.push({ line: lineOf(variant, match.index), kind: HitKinds.fixtureKey });
    }
  }
  return hits;
}

/**
 * The hits in one file's text: fixture key values (any candidate on a line, and PEM blocks), the fixture notice, and a
 * test-only protocol prefix in an appsettings*.json file.
 * @returns {{ line: number, kind: string }[]}
 */
export function scanText(fileName, text, entries) {
  const hits = [];
  const isConfig = CONFIG_FILE.test(fileName);
  text.split(LINE_BREAK).forEach((lineText, index) => {
    const line = index + FIRST_LINE;
    if (lineText.includes(FIXTURE_NOTICE)) hits.push({ line, kind: HitKinds.fixtureDocument });
    if (isConfig && TEST_PROTOCOL_PREFIXES.some((prefix) => lineText.includes(prefix))) hits.push({ line, kind: HitKinds.testProtocolPrefix });
    if ([...candidateValues(lineText)].some((candidate) => isFixtureValue(candidate, entries))) hits.push({ line, kind: HitKinds.fixtureKey });
  });
  return [...hits, ...pemHits(text, entries)];
}

function* filesUnder(folder) {
  for (const entry of readdirSync(folder, { withFileTypes: true })) {
    const path = join(folder, entry.name);
    if (entry.isDirectory()) {
      if (!SKIPPED_FOLDERS.has(entry.name)) yield* filesUnder(path);
    } else if (entry.isFile() && !SKIPPED_EXTENSIONS.has(extname(entry.name).toLowerCase())) {
      yield path;
    }
  }
}

/**
 * Scans every text file under `root` (gitignored files included: the local secrets files are what matters).
 * `excluded` holds root-relative paths (forward slashes) of files or folders to skip, e.g. the fixture sources.
 * @returns {{ hits: { path: string, line: number, kind: string }[], fileCount: number }}
 */
export function scanTree(root, entries, excluded = []) {
  const hits = [];
  let fileCount = 0;
  const isExcluded = (path) => excluded.some((prefix) => path === prefix || path.startsWith(`${prefix}/`));
  const files = statSync(root).isFile() ? [root] : filesUnder(root);
  for (const file of files) {
    const path = relative(root, file).split(sep).join('/') || file;
    if (isExcluded(path) || statSync(file).size > MAX_FILE_BYTES) continue;
    const text = readFileSync(file, 'utf8');
    if (text.includes(NUL)) continue;
    fileCount += 1;
    const name = file.split(sep).pop();
    for (const hit of scanText(name, text, entries)) hits.push({ path, ...hit });
  }
  return { hits, fileCount };
}
