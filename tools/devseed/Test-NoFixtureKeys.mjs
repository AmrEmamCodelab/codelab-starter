#!/usr/bin/env node
// Fails when a tree holds the starter's TEST-ONLY fixture key material (the golden vector keys), a fixture key file,
// or a test-only protocol prefix (CODELABTEST, ALTPFX) in an appsettings*.json file. Run it on an init-ed project and
// on its local secrets files: a fork must only ever hold keys generated for it.
//
//   node tools/devseed/Test-NoFixtureKeys.mjs <path> [--digests <file>] [--exclude <relative,paths>]
//   node tools/devseed/Test-NoFixtureKeys.mjs --refresh-digests     copy the published fixture digests next to this tool
//
// Exit 0 = clean, 1 = hits (path:line and kind, never the value), 2 = usage or error.
import { writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, runCli, UsageError } from '../extract/lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from '../extract/lib/exit-codes.mjs';
import { DIGESTS_PATH, ENTRIES_PROPERTY, loadEntries, PUBLISHED_FIXTURES_PATH, readEntries, scanTree } from './lib/fixture-key-scan.mjs';

const TOOL_NAME = 'Test-NoFixtureKeys';
const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const LIST_SEPARATOR = ',';
const JSON_INDENT = 2;
const LF = '\n';
/** The fixture sources and their generator, skipped when they sit under the scanned root (the starter repository). */
export const DEFAULT_EXCLUDED = Object.freeze(['packages/vectors', 'tools/vectors', PUBLISHED_FIXTURES_PATH, DIGESTS_PATH]);
const DIGESTS_DESCRIPTION = `Copy of ${PUBLISHED_FIXTURES_PATH} (digests of digests of the TEST-ONLY fixture keys, never values), `
  + 'kept here so an init-ed project without packages/ can run Test-NoFixtureKeys. Written by: node tools/devseed/Test-NoFixtureKeys.mjs --refresh-digests.';

function refreshDigests() {
  const entries = [...readEntries(join(REPO_ROOT, PUBLISHED_FIXTURES_PATH))].sort();
  const document = { description: DIGESTS_DESCRIPTION, source: PUBLISHED_FIXTURES_PATH, [ENTRIES_PROPERTY]: entries };
  writeFileSync(join(REPO_ROOT, DIGESTS_PATH), `${JSON.stringify(document, null, JSON_INDENT)}${LF}`);
  console.log(`${TOOL_NAME}: wrote ${DIGESTS_PATH} (${entries.length} entries)`);
  return EXIT_CLEAN;
}

function scan(target, options) {
  const extra = typeof options.exclude === 'string' ? options.exclude.split(LIST_SEPARATOR).filter(Boolean) : [];
  const entries = loadEntries(REPO_ROOT, typeof options.digests === 'string' ? options.digests : null);
  const { hits, fileCount } = scanTree(resolve(target), entries, [...DEFAULT_EXCLUDED, ...extra]);
  for (const hit of hits) console.log(`${hit.path}:${hit.line}: ${hit.kind}`);
  console.log(`${TOOL_NAME}: ${fileCount} files, ${hits.length} fixture hits`);
  return hits.length === 0 ? EXIT_CLEAN : EXIT_FINDINGS;
}

await runCli(TOOL_NAME, () => {
  const { positionals, options } = parseArgs(process.argv.slice(2));
  if (options['refresh-digests'] === true) return refreshDigests();
  const [target] = positionals;
  if (!target) throw new UsageError('expected a path to scan (or --refresh-digests)');
  return scan(target, options);
});
