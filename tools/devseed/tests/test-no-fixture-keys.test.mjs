// Test-NoFixtureKeys: fixture key material is found in every spelling a config may hold, fresh local secrets are clean.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runScript, makeTempDir } from '../../fork/tests/support/powershell.mjs';
import {
  DIGESTS_PATH, ENTRIES_PROPERTY, HitKinds, loadEntries, PUBLISHED_FIXTURES_PATH, readEntries, scanText,
} from '../lib/fixture-key-scan.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, '..', '..', '..');
const CLI = join(REPO_ROOT, 'tools', 'devseed', 'Test-NoFixtureKeys.mjs');
const SECRETS_SCRIPT = join(REPO_ROOT, 'tools', 'devseed', 'New-LocalSecrets.ps1');
const FIXTURE_KEYS = join(REPO_ROOT, 'packages', 'vectors', 'fixture-keys');
const LOCAL_FILE = 'appsettings.Development.local.json';
const EXIT_CLEAN = 0;
const EXIT_HITS = 1;
const EXIT_USAGE = 2;

const ENTRIES = loadEntries(REPO_ROOT, null);

function fixture(file) {
  return JSON.parse(readFileSync(join(FIXTURE_KEYS, file), 'utf8'));
}

function runCli(args) {
  const result = spawnSync(process.execPath, [CLI, ...args], { encoding: 'utf8', cwd: REPO_ROOT });
  return { code: result.status, output: `${result.stdout}${result.stderr}` };
}

function withTemp(body) {
  const temp = makeTempDir();
  try {
    body(temp.path);
  } finally {
    temp.cleanup();
  }
}

function kinds(hits) {
  return hits.map((hit) => hit.kind);
}

test('a fixture ring key in a config file is a hit, in base64 and in hex', () => {
  const material = fixture('public-id.json').ring.keys[0].materialBase64;
  const config = JSON.stringify({ Codelab: { Security: { PublicIds: { Keys: [{ MaterialBase64: material }] } } } });
  assert.deepEqual(kinds(scanText(LOCAL_FILE, config, ENTRIES)), [HitKinds.fixtureKey]);
  const hex = Buffer.from(material, 'base64').toString('hex');
  assert.deepEqual(kinds(scanText('keys.txt', `key=${hex}`, ENTRIES)), [HitKinds.fixtureKey]);
});

test('the fixture ES256 private key is a hit as a PEM file and JSON-escaped in a config', () => {
  const pem = fixture('service-assertion.json').privateKeyPem;
  assert.ok(kinds(scanText('client.pem', pem, ENTRIES)).includes(HitKinds.fixtureKey));
  const config = JSON.stringify({ Client: { PrivateKeyPem: pem } }, null, 2);
  assert.ok(kinds(scanText(LOCAL_FILE, config, ENTRIES)).includes(HitKinds.fixtureKey));
});

test('a copied fixture file and a test protocol prefix in appsettings are hits; the prefix in code is not', () => {
  const copied = JSON.stringify(fixture('signing.json'), null, 2);
  assert.ok(kinds(scanText('signing.json', copied, ENTRIES)).includes(HitKinds.fixtureDocument));
  const settings = JSON.stringify({ Codelab: { Security: { Protocol: { Prefix: 'CODELABTEST' } } } });
  assert.deepEqual(kinds(scanText('appsettings.Production.json', settings, ENTRIES)), [HitKinds.testProtocolPrefix]);
  assert.deepEqual(scanText('ProtocolTests.cs', 'var prefix = "ALTPFX";', ENTRIES), []);
});

test('the CLI reports path and line, never the value, and exits 1 on a hit', () => {
  withTemp((root) => {
    const material = fixture('signing.json').ring.keys[0].materialBase64;
    mkdirSync(join(root, 'api'));
    writeFileSync(join(root, 'api', LOCAL_FILE), `{\n  "Key": "${material}"\n}\n`);
    const result = runCli([root]);
    assert.equal(result.code, EXIT_HITS, result.output);
    assert.match(result.output, /api\/appsettings\.Development\.local\.json:2: fixture-key/);
    assert.ok(!result.output.includes(material), 'never prints the value');
  });
});

test('fresh local secrets of two runs hold no fixture key and differ from each other', () => {
  withTemp((root) => {
    const files = ['first', 'second'].map((run) => {
      const folders = ['api', 'service', 'gateway', 'ops'].map((name) => join(root, run, name));
      folders.forEach((folder) => mkdirSync(folder, { recursive: true }));
      const [api, service, gateway, ops] = folders.map((folder) => join(folder, LOCAL_FILE));
      const result = runScript(SECRETS_SCRIPT, ['-Path', api, '-ServicePath', service, '-GatewayPath', gateway, '-OpsPath', ops, '-StorageRoot', join(root, run, 'store')], { cwd: REPO_ROOT });
      assert.equal(result.code, EXIT_CLEAN, result.output);
      return [api, ops].map((file) => readFileSync(file, 'utf8'));
    });
    const scan = runCli([root]);
    assert.equal(scan.code, EXIT_CLEAN, scan.output);
    assert.notEqual(files[0][0], files[1][0], 'the API files differ');
    assert.notEqual(files[0][1], files[1][1], 'the Ops Console files differ');
  });
});

test('the digest copy kept with the tool matches the published fixture digests', () => {
  const published = [...readEntries(join(REPO_ROOT, PUBLISHED_FIXTURES_PATH))].sort();
  const copy = JSON.parse(readFileSync(join(REPO_ROOT, DIGESTS_PATH), 'utf8'))[ENTRIES_PROPERTY];
  assert.deepEqual(copy, published, 'rerun: node tools/devseed/Test-NoFixtureKeys.mjs --refresh-digests');
});

test('the CLI without a path is a usage error', () => {
  assert.equal(runCli([]).code, EXIT_USAGE);
});
