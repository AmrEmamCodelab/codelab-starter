// The committed manifest's batches and their provenance origin files agree, and harvest batches never stage vector
// values, key fixtures, key files, settings or national-integration code (checked on what was really staged).
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { test } from 'node:test';
import { loadManifest } from '../lib/manifest.mjs';
import { MANIFEST_FILE, REPO_ROOT, TESTS_DIRECTORY } from './helpers.mjs';

const ORIGINS_DIRECTORY = join(REPO_ROOT, 'provenance', 'origins');
const ORIGINS_EXTENSION = '.json';
const PROMOTION_STATES = new Set(['staged', 'rewritten']);
const SPIKE_BATCHES = {
  'pkg-spike-security': 'src/Codelab.Security/**',
  'pkg-spike-observability': 'src/Codelab.Observability/**',
  'pkg-spike-sample': 'sample/**',
};
const HARVEST_BATCHES = ['p0', 'p1-security', 'p1-observability-errors', 'p1-integration', 'p1-testing', 'p1-ng-security'];
const RETIRED_BATCHES = ['pkg-spike'];

const manifest = loadManifest(MANIFEST_FILE);
const exclusions = JSON.parse(readFileSync(join(TESTS_DIRECTORY, 'fixtures', 'harvest-exclusions.json'), 'utf8'));
const forbidden = exclusions.forbiddenSourcePatterns.map((rule) => ({ ...rule, regex: new RegExp(rule.pattern, rule.flags) }));

/** Every provenance/origins/<batch>.json, keyed by file name without the extension. */
function loadOriginFiles() {
  if (!existsSync(ORIGINS_DIRECTORY)) return new Map();
  return new Map(readdirSync(ORIGINS_DIRECTORY)
    .filter((name) => name.endsWith(ORIGINS_EXTENSION))
    .map((name) => [basename(name, ORIGINS_EXTENSION), JSON.parse(readFileSync(join(ORIGINS_DIRECTORY, name), 'utf8'))]));
}

const originFiles = loadOriginFiles();

test('the spike is split into one batch per owner, all on the same pinned source', () => {
  const sources = Object.keys(SPIKE_BATCHES).map((name) => manifest.batches[name]?.source);
  for (const [name, glob] of Object.entries(SPIKE_BATCHES)) {
    assert.ok(manifest.batches[name], `batch ${name} is declared`);
    assert.deepEqual(manifest.batches[name].entries.map((entry) => entry.source), [glob]);
  }
  assert.match(sources[0].contentDigest, /^[0-9a-f]{64}$/);
  for (const source of sources) assert.deepEqual(source, sources[0]);
});

test('the retired combined spike batch is gone from the manifest and from provenance', () => {
  for (const name of RETIRED_BATCHES) {
    assert.equal(manifest.batches[name], undefined, `batch ${name} is retired`);
    assert.equal(originFiles.has(name), false, `provenance/origins/${name}.json is retired`);
  }
});

test('harvest batches read the pinned harvest commit (no own source block)', () => {
  for (const name of HARVEST_BATCHES) {
    assert.ok(manifest.batches[name], `batch ${name} is declared`);
    assert.equal(manifest.batches[name].source, undefined, `batch ${name} must not override the source`);
  }
});

test('entry ids are unique within each batch', () => {
  for (const [name, batch] of Object.entries(manifest.batches)) {
    const ids = batch.entries.map((entry) => entry.id);
    assert.equal(new Set(ids).size, ids.length, `batch ${name} has duplicate entry ids`);
  }
});

test('every manifest batch has an origin file and every origin file names a manifest batch', () => {
  assert.deepEqual([...originFiles.keys()].sort(), Object.keys(manifest.batches).sort());
});

test('every origin entry belongs to its batch, names a declared entry and has a known promotion state', () => {
  for (const [fileName, document] of originFiles) {
    const entryIds = new Set(manifest.batches[fileName].entries.map((entry) => entry.id));
    assert.equal(document.batch, fileName, `${fileName}.json names batch ${document.batch}`);
    assert.ok(document.origins.length > 0, `${fileName}.json lists no origin`);
    for (const origin of document.origins) {
      assert.equal(origin.batch, fileName, `${origin.source}: batch`);
      assert.ok(entryIds.has(origin.entryId), `${origin.source}: unknown entry ${origin.entryId}`);
      assert.ok(PROMOTION_STATES.has(origin.promotion), `${origin.source}: promotion ${origin.promotion}`);
    }
  }
});

test('origin files record the source the manifest pins', () => {
  for (const [fileName, document] of originFiles) {
    const own = manifest.batches[fileName].source;
    if (own) {
      assert.equal(document.sourceCommit, null, fileName);
      assert.equal(document.sourceContentDigest, own.contentDigest, fileName);
    } else {
      assert.equal(document.sourceCommit, manifest.source.commit, fileName);
    }
  }
});

test('no harvest batch staged a vector, key fixture, key file, settings file or national-integration file', () => {
  for (const name of HARVEST_BATCHES) {
    for (const origin of originFiles.get(name)?.origins ?? []) {
      const hit = forbidden.find((rule) => rule.regex.test(origin.source));
      assert.equal(hit, undefined, `${name}: ${origin.source} must not be harvested (${hit?.reason})`);
    }
  }
});

test('the exclusion rules flag the shapes they describe', () => {
  for (const path of exclusions.flaggedSamples) assert.ok(forbidden.some((rule) => rule.regex.test(path)), path);
  for (const path of exclusions.allowedSamples) assert.ok(!forbidden.some((rule) => rule.regex.test(path)), path);
});
