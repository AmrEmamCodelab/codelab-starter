#!/usr/bin/env node
// Generates provenance/SOURCE.md: the source commit and tree hash, the harvest manifest check (every harvest file's
// `git hash-object --no-filters` against `git ls-tree -r <commit>`), the per-file origin map from
// provenance/origins/*.json and the hard-deny list. Uses read-only git commands only.
//
//   node tools/extract/generate-source.mjs [--manifest <file>] [--out <file>] [--origins <dir>] [--date YYYY-MM-DD]
//
// Exit 0 = written and 100% match, 1 = written but the manifest check failed, 2 = usage or internal error.
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, runCli } from './lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from './lib/exit-codes.mjs';
import { listAllFiles } from './lib/files.mjs';
import { loadManifest } from './lib/manifest.mjs';
import { gitBlobId } from './lib/hashing.mjs';
import {
  classifyLineEndingMismatches, compareHarvest, isFullMatch, parseLsTree, renderSourceMarkdown,
} from './lib/provenance.mjs';

const TOOL_NAME = 'generate-source';
const TOOL_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(TOOL_DIRECTORY, '..', '..');
const DEFAULT_MANIFEST = join(TOOL_DIRECTORY, 'allowlist.manifest.yaml');
const DEFAULT_OUTPUT = join(REPO_ROOT, 'provenance', 'SOURCE.md');
const DEFAULT_ORIGINS_DIRECTORY = join(REPO_ROOT, 'provenance', 'origins');
const ORIGINS_FILE_EXTENSION = '.json';
const ISO_DATE_LENGTH = 10;
const GIT_MAX_BUFFER_BYTES = 512 * 1024 * 1024;
const CRLF = /\r\n/g;
const LF = '\n';

function git(args, { cwd, input } = {}) {
  return execFileSync('git', args, { cwd, input, maxBuffer: GIT_MAX_BUFFER_BYTES, stdio: ['pipe', 'pipe', 'pipe'] })
    .toString('utf8');
}

/** git blob id of every harvest file, computed by git itself in one process (--stdin-paths). */
function hashHarvest(harvestRoot) {
  const paths = listAllFiles(harvestRoot);
  const ids = git(['hash-object', '--no-filters', '--stdin-paths'], { cwd: harvestRoot, input: `${paths.join('\n')}\n` })
    .trim().split(/\r?\n/);
  if (ids.length !== paths.length) throw new Error(`hash-object returned ${ids.length} ids for ${paths.length} files`);
  return new Map(paths.map((path, index) => [path, ids[index]]));
}

function lfNormalizedBlobId(harvestRoot) {
  return (path) => gitBlobId(Buffer.from(readFileSync(join(harvestRoot, path)).toString('latin1').replace(CRLF, LF), 'latin1'));
}

function loadOrigins(originsDirectory) {
  if (!existsSync(originsDirectory)) return [];
  return readdirSync(originsDirectory)
    .filter((name) => name.endsWith(ORIGINS_FILE_EXTENSION))
    .flatMap((name) => JSON.parse(readFileSync(join(originsDirectory, name), 'utf8')).origins ?? []);
}

function printComparison(comparison) {
  const lineEndingOnly = comparison.mismatched.filter((item) => item.lineEndingsOnly).length;
  console.log(`${TOOL_NAME}: ${comparison.matched}/${comparison.total} tree files match byte for byte; `
    + `${comparison.mismatched.length} mismatched (${lineEndingOnly} line endings only), ${comparison.missing.length} missing, `
    + `${comparison.extra.length} extra, ${comparison.nonBlob.length} non-blob.`);
  for (const item of comparison.mismatched.filter((entry) => !entry.lineEndingsOnly)) console.log(`  MISMATCH ${item.path}`);
  for (const path of comparison.missing) console.log(`  MISSING  ${path}`);
  for (const path of comparison.extra) console.log(`  EXTRA    ${path}`);
}

await runCli(TOOL_NAME, () => {
  const { options } = parseArgs(process.argv.slice(2));
  const manifest = loadManifest(resolve(typeof options.manifest === 'string' ? options.manifest : DEFAULT_MANIFEST));
  const output = resolve(typeof options.out === 'string' ? options.out : DEFAULT_OUTPUT);
  const originsDirectory = resolve(typeof options.origins === 'string' ? options.origins : DEFAULT_ORIGINS_DIRECTORY);
  const generatedOn = typeof options.date === 'string' ? options.date : new Date().toISOString().slice(0, ISO_DATE_LENGTH);
  const { repository, commit, harvestRoot } = manifest.source;

  const resolvedCommit = git(['-C', repository, 'rev-parse', `${commit}^{commit}`]).trim();
  const resolvedTree = git(['-C', repository, 'rev-parse', `${commit}^{tree}`]).trim();
  const treeEntries = parseLsTree(git(['-C', repository, 'ls-tree', '-r', '-z', '--full-tree', resolvedCommit]));
  const strict = compareHarvest(treeEntries, hashHarvest(harvestRoot));
  const comparison = { ...strict, mismatched: classifyLineEndingMismatches(strict.mismatched, lfNormalizedBlobId(harvestRoot)) };

  const markdown = renderSourceMarkdown({
    source: manifest.source, resolvedCommit, resolvedTree, generatedOn, comparison,
    origins: loadOrigins(originsDirectory), hardDeny: manifest.hardDeny,
  });
  mkdirSync(dirname(output), { recursive: true });
  writeFileSync(output, markdown);
  printComparison(comparison);
  console.log(`${TOOL_NAME}: tree ${resolvedTree}${resolvedTree === manifest.source.treeHash ? '' : ' (DIFFERS from manifest)'} -> ${output}`);
  const passed = isFullMatch(comparison) && resolvedTree === manifest.source.treeHash;
  return passed ? EXIT_CLEAN : EXIT_FINDINGS;
});
