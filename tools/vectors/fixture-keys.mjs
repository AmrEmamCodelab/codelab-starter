#!/usr/bin/env node
// The starter's TEST-ONLY fixture keys and their published-material denylist entries.
//
//   node tools/vectors/fixture-keys.mjs generate [--force]       fresh keys -> packages/vectors/fixture-keys/*.json
//   node tools/vectors/fixture-keys.mjs published-material        write published-material.fixtures.json
//   node tools/vectors/fixture-keys.mjs published-material --check   exit 1 when that file is stale
//
// Exit 0 = done / current, 1 = stale, 2 = usage or internal error. After `generate --force`, regenerate every vector
// file (see tools/vectors/README.md): the checked-in vectors are pinned to these keys.
import { writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, runCli, UsageError } from '../extract/lib/cli-args.mjs';
import { EXIT_CLEAN, EXIT_FINDINGS } from '../extract/lib/exit-codes.mjs';
import { generateFixtureDocuments, readFixtureDocuments, writeFixtureDocuments } from './lib/fixture-keys.mjs';
import {
  FIXTURE_KEYS_PATH, isPublishedFixturesFileCurrent, PUBLISHED_FIXTURES_PATH, publishedFixturesText,
} from './lib/published-fixtures.mjs';

const TOOL_NAME = 'fixture-keys';
const REPO_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
export const Commands = Object.freeze({ generate: 'generate', publishedMaterial: 'published-material' });

function generate(options) {
  const written = writeFixtureDocuments(join(REPO_ROOT, FIXTURE_KEYS_PATH), generateFixtureDocuments(), { force: options.force === true });
  for (const path of written) console.log(`${TOOL_NAME}: wrote ${path}`);
  console.log(`${TOOL_NAME}: now run "node tools/vectors/fixture-keys.mjs published-material" and regenerate the vectors.`);
  return EXIT_CLEAN;
}

function publishedMaterial(options) {
  const documents = readFixtureDocuments(join(REPO_ROOT, FIXTURE_KEYS_PATH));
  const target = join(REPO_ROOT, PUBLISHED_FIXTURES_PATH);
  if (options.check === true) {
    const current = isPublishedFixturesFileCurrent(target, documents);
    console.log(`${TOOL_NAME}: ${PUBLISHED_FIXTURES_PATH} is ${current ? 'current' : 'STALE (rerun without --check)'}`);
    return current ? EXIT_CLEAN : EXIT_FINDINGS;
  }
  writeFileSync(target, publishedFixturesText(documents));
  console.log(`${TOOL_NAME}: wrote ${target}`);
  return EXIT_CLEAN;
}

await runCli(TOOL_NAME, () => {
  const { positionals, options } = parseArgs(process.argv.slice(2));
  const [command] = positionals;
  if (command === Commands.generate) return generate(options);
  if (command === Commands.publishedMaterial) return publishedMaterial(options);
  throw new UsageError(`expected a command: ${Object.values(Commands).join(' | ')}`);
});
