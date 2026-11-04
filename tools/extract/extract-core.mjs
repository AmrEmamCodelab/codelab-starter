#!/usr/bin/env node
// The copy step of extract.ps1: resolves one manifest batch against the harvest copy, enforces hard-deny, copies the
// allowed files to <staging>/files and writes <staging>/origins.json. Scanning is extract.ps1's next step.
//
//   node tools/extract/extract-core.mjs --batch p0 --staging <dir> [--manifest <file>] [--harvest <dir>]
//
// Exit 0 = staged, 1 = an allowlist entry matched no file (stale manifest), 2 = usage or internal error.
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, requireOption, runCli, UsageError } from './lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from './lib/exit-codes.mjs';
import { stageCopies } from './lib/extractor.mjs';
import { listAllFiles, listFiles } from './lib/files.mjs';
import { contentDigest } from './lib/hashing.mjs';
import { loadManifest, resolveBatch } from './lib/manifest.mjs';

const TOOL_NAME = 'extract-core';
const TOOL_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const DEFAULT_MANIFEST = join(TOOL_DIRECTORY, 'allowlist.manifest.yaml');
const STAGING_FILES_FOLDER = 'files';
const ORIGINS_FILE = 'origins.json';
const JSON_INDENT = 2;

/**
 * A batch reads the manifest's harvest copy unless it names its own source (a non-git input such as the package
 * spike). Such a source must carry a contentDigest, and the folder must still match it.
 */
function resolveBatchSource(manifest, batchName, harvestOverride) {
  const own = manifest.batches?.[batchName]?.source;
  const harvestRoot = resolve(harvestOverride ?? own?.harvestRoot ?? manifest.source.harvestRoot);
  if (!own) return { harvestRoot, sourceCommit: manifest.source.commit, sourceContentDigest: null };
  const files = listAllFiles(harvestRoot).map((path) => ({ path, bytes: readFileSync(join(harvestRoot, path)) }));
  const actual = contentDigest(files);
  if (actual !== own.contentDigest) {
    throw new Error(`batch ${batchName}: ${harvestRoot} does not match source.contentDigest (expected ${own.contentDigest}, got ${actual})`);
  }
  return { harvestRoot, sourceCommit: null, sourceContentDigest: actual };
}

function ensureEmptyStaging(staging) {
  if (existsSync(staging) && readdirSync(staging).length > 0) {
    throw new UsageError(`staging folder ${staging} is not empty (extract.ps1 -Clean empties it)`);
  }
  mkdirSync(join(staging, STAGING_FILES_FOLDER), { recursive: true });
}

await runCli(TOOL_NAME, () => {
  const { options } = parseArgs(process.argv.slice(2));
  const batchName = requireOption(options, 'batch');
  const staging = resolve(requireOption(options, 'staging'));
  const manifest = loadManifest(resolve(typeof options.manifest === 'string' ? options.manifest : DEFAULT_MANIFEST));
  const { harvestRoot, sourceCommit, sourceContentDigest } = resolveBatchSource(
    manifest, batchName, typeof options.harvest === 'string' ? options.harvest : undefined,
  );

  const { copies, denied, emptyEntries } = resolveBatch(manifest, batchName, listFiles(harvestRoot));
  ensureEmptyStaging(staging);
  const origins = stageCopies({ batchName, harvestRoot, stagingFiles: join(staging, STAGING_FILES_FOLDER), copies });
  const document = { batch: batchName, sourceCommit, sourceRoot: harvestRoot, sourceContentDigest, origins, denied };
  writeFileSync(join(staging, ORIGINS_FILE), `${JSON.stringify(document, null, JSON_INDENT)}\n`);

  console.log(`${TOOL_NAME}: batch ${batchName}: ${origins.length} file(s) staged, ${denied.length} hard-denied.`);
  for (const entry of denied) console.log(`  DENIED ${entry.source}  (${entry.pattern})`);
  for (const entryId of emptyEntries) console.log(`  EMPTY  allowlist entry "${entryId}" matched no harvest file`);
  return emptyEntries.length === 0 ? EXIT_CLEAN : EXIT_FINDINGS;
});
