#!/usr/bin/env node
// Builds tools/extract/secret-digests.json: SHA-256 digests of the key material found in the harvest copy
// (appsettings secrets and connection strings, golden-vector keys, fixture keys in tests, and secret-shaped values
// in compose files, CI workflows, scripts, SQL, launchSettings and environment files). Each value is hashed as text
// and as the key bytes it decodes to (lib/key-material.mjs), so re-encodings match. Only digests are written.
// Run it once per harvest commit, by the platform owner; the harvest itself is only read.
//
//   node tools/extract/build-secret-digests.mjs [--manifest <file>] [--harvest <dir>] [--out <file>]
import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseArgs, runCli } from './lib/cli-args.mjs';
import {
  digestInputFor, secretsFromConfig, secretsFromFixtureCode, secretsFromScript, secretsFromVectors,
} from './lib/digest-sources.mjs';
import { EXIT_CLEAN } from './lib/exit-codes.mjs';
import { listFiles } from './lib/files.mjs';
import { matchesGlob } from './lib/glob.mjs';
import { digestsOf } from './lib/key-material.mjs';
import { loadManifest } from './lib/manifest.mjs';
import { DIGEST_ALGORITHM } from './lib/secret-scanner.mjs';

const TOOL_NAME = 'build-secret-digests';
const TOOL_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(TOOL_DIRECTORY, '..', '..');
const DEFAULT_MANIFEST = join(TOOL_DIRECTORY, 'allowlist.manifest.yaml');
const JSON_INDENT = 2;

function collect(harvestRoot, files, globs, extract) {
  const found = [];
  for (const path of files.filter((candidate) => globs.some((glob) => matchesGlob(candidate, glob)))) {
    try {
      found.push(...extract(readFileSync(join(harvestRoot, path), 'utf8')));
    } catch (error) {
      // The parser's message quotes file content, which may be a secret: report only the file and the error type.
      throw new Error(`cannot read key material from ${path} (${error.name})`);
    }
  }
  return found;
}

function toDigestList(secrets) {
  const byDigest = new Map();
  for (const { value, kind } of secrets) {
    for (const digest of digestsOf(digestInputFor(value))) {
      if (!byDigest.has(digest)) byDigest.set(digest, kind);
    }
  }
  return [...byDigest.entries()]
    .map(([sha256, kind]) => ({ sha256, kind }))
    .sort((left, right) => left.sha256.localeCompare(right.sha256));
}

await runCli(TOOL_NAME, () => {
  const { options } = parseArgs(process.argv.slice(2));
  const manifest = loadManifest(resolve(typeof options.manifest === 'string' ? options.manifest : DEFAULT_MANIFEST));
  const harvestRoot = resolve(typeof options.harvest === 'string' ? options.harvest : manifest.source.harvestRoot);
  const sources = manifest.secretDigests;
  const output = resolve(typeof options.out === 'string' ? options.out : join(REPO_ROOT, sources.output));

  const files = listFiles(harvestRoot);
  const secrets = [
    ...collect(harvestRoot, files, sources.configFiles, secretsFromConfig),
    ...collect(harvestRoot, files, sources.vectorFiles, secretsFromVectors),
    ...collect(harvestRoot, files, sources.fixtureFiles, secretsFromFixtureCode),
    ...collect(harvestRoot, files, sources.scriptFiles ?? [], (text) => secretsFromScript(text)),
  ];
  const digests = toDigestList(secrets);
  const document = {
    description: 'SHA-256 digests of known source-product key material. Values are never stored. '
      + 'Regenerate with tools/extract/build-secret-digests.mjs.',
    algorithm: DIGEST_ALGORITHM,
    sourceCommit: manifest.source.commit,
    digests,
  };
  writeFileSync(output, `${JSON.stringify(document, null, JSON_INDENT)}\n`);
  const kinds = Object.entries(Object.groupBy(digests, (entry) => entry.kind))
    .map(([kind, entries]) => `${kind}=${entries.length}`).join(', ');
  console.log(`${TOOL_NAME}: ${digests.length} digest(s) from ${files.length} harvest file(s) -> ${output} (${kinds})`);
  return EXIT_CLEAN;
});
