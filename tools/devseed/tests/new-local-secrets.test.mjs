// Runs tools/devseed/New-LocalSecrets.ps1 the way a developer does (Windows PowerShell 5.1) against a temp target.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, writeFileSync, existsSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runScript, makeTempDir } from '../../fork/tests/support/powershell.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, '..', '..', '..');
const SCRIPT = join(REPO_ROOT, 'tools', 'devseed', 'New-LocalSecrets.ps1');
const VECTORS = join(REPO_ROOT, 'packages', 'vectors');
const FILE_NAME = 'appsettings.Development.local.json';
const EXIT_OK = 0;
const EXIT_REFUSED = 1;
const MIN_KEY_BYTES = 32;
const MIN_PASSWORD_LENGTH = 16;
const KEY_ID_PATTERN = /^[a-z0-9]{1,8}$/;

function run(target, extra = []) {
  return runScript(SCRIPT, ['-Path', target, ...extra], { cwd: REPO_ROOT });
}

function read(target) {
  return JSON.parse(readFileSync(target, 'utf8'));
}

function assertKey(value, label) {
  assert.equal(typeof value, 'string', `${label} is a string`);
  assert.ok(Buffer.from(value, 'base64').length >= MIN_KEY_BYTES, `${label} decodes to at least ${MIN_KEY_BYTES} bytes`);
}

function assertRing(ring, label) {
  assert.match(ring.ActiveKeyId, KEY_ID_PATTERN, `${label} key id`);
  assert.equal(ring.Keys.length, 1, `${label} has one key`);
  assert.equal(ring.Keys[0].KeyId, ring.ActiveKeyId, `${label} active key is listed`);
  assertKey(ring.Keys[0].MaterialBase64, `${label} material`);
}

/** Every generated secret value, for comparisons. */
function secretsOf(doc) {
  const security = doc.Codelab.Security;
  return [
    doc.Identity.BreakGlass.Password,
    doc.Identity.DevUsers[0].Password,
    doc.Identity.Jwt.SigningKey,
    security.PublicIds.Keys[0].MaterialBase64,
    security.RequestSigning.Keys[0].MaterialBase64,
    doc.Observability.UserIdHash.Key,
  ];
}

function filesUnder(folder) {
  return readdirSync(folder).flatMap((name) => {
    const path = join(folder, name);
    return statSync(path).isDirectory() ? filesUnder(path) : [path];
  });
}

test('writes the contract shape with strong values and prints the break-glass user name only', () => {
  const temp = makeTempDir();
  try {
    const target = join(temp.path, FILE_NAME);
    const result = run(target);
    assert.equal(result.code, EXIT_OK, result.output);
    const doc = read(target);
    assert.equal(typeof doc.Identity.BreakGlass.UserName, 'string');
    assert.ok(doc.Identity.BreakGlass.Password.length >= MIN_PASSWORD_LENGTH);
    assert.equal(typeof doc.Identity.DevUsers[0].UserName, 'string');
    assert.ok(doc.Identity.DevUsers[0].Password.length >= MIN_PASSWORD_LENGTH);
    assertKey(doc.Identity.Jwt.SigningKey, 'session signing key');
    assertRing(doc.Codelab.Security.PublicIds, 'PublicIds');
    assertRing(doc.Codelab.Security.RequestSigning, 'RequestSigning');
    assertKey(doc.Observability.UserIdHash.Key, 'user hash key');
    assert.ok(result.output.includes(doc.Identity.BreakGlass.UserName), 'prints the break-glass user name');
    for (const secret of secretsOf(doc)) assert.ok(!result.output.includes(secret), 'never prints a secret');
    assert.equal(new Set(secretsOf(doc)).size, secretsOf(doc).length, 'every value is distinct');
  } finally {
    temp.cleanup();
  }
});

test('two runs produce different values', () => {
  const temp = makeTempDir();
  try {
    const first = join(temp.path, 'first.json');
    const second = join(temp.path, 'second.json');
    assert.equal(run(first).code, EXIT_OK);
    assert.equal(run(second).code, EXIT_OK);
    const a = secretsOf(read(first));
    const b = secretsOf(read(second));
    a.forEach((value, index) => assert.notEqual(value, b[index], `value ${index} differs between runs`));
  } finally {
    temp.cleanup();
  }
});

test('refuses to overwrite an existing file without -Force and regenerates with it', () => {
  const temp = makeTempDir();
  try {
    const target = join(temp.path, FILE_NAME);
    const original = '{"kept":true}';
    writeFileSync(target, original);
    const refused = run(target);
    assert.equal(refused.code, EXIT_REFUSED, refused.output);
    assert.equal(readFileSync(target, 'utf8'), original, 'the file is untouched');
    const forced = run(target, ['-Force']);
    assert.equal(forced.code, EXIT_OK, forced.output);
    assert.ok(read(target).Identity, 'the file was regenerated');
  } finally {
    temp.cleanup();
  }
});

test('never writes a value that appears in the committed fixtures', () => {
  const temp = makeTempDir();
  try {
    const target = join(temp.path, FILE_NAME);
    assert.equal(run(target).code, EXIT_OK);
    assert.ok(existsSync(VECTORS), 'the vectors folder exists');
    const fixtures = filesUnder(VECTORS).map((path) => readFileSync(path, 'utf8')).join('\n');
    for (const secret of secretsOf(read(target))) assert.ok(!fixtures.includes(secret), 'value is not a fixture value');
  } finally {
    temp.cleanup();
  }
});
