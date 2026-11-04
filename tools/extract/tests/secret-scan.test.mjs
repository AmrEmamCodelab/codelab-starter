import assert from 'node:assert/strict';
import { test } from 'node:test';
import { join } from 'node:path';
import { EXIT_CLEAN, EXIT_FINDINGS } from '../lib/exit-codes.mjs';
import { sha256Hex } from '../lib/hashing.mjs';
import {
  candidateValues, DIGEST_RULE_ID, loadDigestDenylist, normalizePemBody, scanTextForSecrets, scanTreeForSecrets,
} from '../lib/secret-scanner.mjs';
import { DIGEST_FILE, joined, makeTempDirectory, REPO_ROOT, runTool, writeTree } from './helpers.mjs';

// Secret-shaped inputs are assembled at run time so this file never contains one in scannable form.
const PEM_BEGIN = joined('-----BEGIN', ' RSA PRIVATE', ' KEY-----');
const PEM_END = joined('-----END', ' RSA PRIVATE', ' KEY-----');
const FIXTURE_SECRET = joined('Zq8', '-starter-fixture-', 'value-7Hk');
const NO_DIGESTS = new Map();
const OWN_FOLDER = 'tools/extract/';

const ruleIds = (text, digests = NO_DIGESTS) => scanTextForSecrets('x.txt', text, digests).map((finding) => finding.ruleId);

test('private key blocks are reported', () => {
  assert.deepEqual(ruleIds(`${PEM_BEGIN}\nAAAA\n${PEM_END}\n`), ['private-key']);
});

test('provider tokens are reported', () => {
  assert.deepEqual(ruleIds(joined('key = AKIA', 'ABCDEFGHIJKLMNOP')), ['aws-access-key']);
  assert.deepEqual(ruleIds(joined('token: ghp_', 'a'.repeat(36))), ['github-token']);
});

test('a literal password in a connection string is reported and redacted', () => {
  const literal = joined('Sup3r', 'Secret!');
  const findings = scanTextForSecrets('appsettings.json', `"Db": "Server=db;User Id=app;Pass${'word'}=${literal};"`, NO_DIGESTS);
  assert.deepEqual(findings.map((finding) => finding.ruleId), ['connection-string-password']);
  assert.ok(!findings[0].excerpt.includes(literal), 'the secret must not be printed');
});

test('secret-named settings with literal values are reported; placeholders and identifiers are not', () => {
  assert.deepEqual(ruleIds(joined('"Client', 'Secret": "', 'q9X2-kLm4-Pz77"')), ['secret-assignment']);
  assert.deepEqual(ruleIds(joined('"Client', 'Secret": ""')), []);
  assert.deepEqual(ruleIds(joined('"Client', 'Secret": "${SSO_CLIENT_SECRET}"')), []);
  assert.deepEqual(ruleIds(joined('"Client', 'Secret": "<from user-secrets>"')), []);
  assert.deepEqual(ruleIds(joined("const KIND_CONFIG_", "SECRET = 'config-secret';")), []);
  assert.deepEqual(ruleIds(joined('"//ClientSec', 'ret": "Leave EMPTY here, supply it by env."')), []);
  assert.deepEqual(ruleIds(joined('Password=', '${DB_PASSWORD};')), []);
});

test('a value whose digest is on the denylist is reported, however it is quoted', () => {
  const digests = new Map([[sha256Hex(FIXTURE_SECRET), 'config-secret']]);
  assert.deepEqual(ruleIds(`"Key": "${FIXTURE_SECRET}"`, digests), [DIGEST_RULE_ID]);
  assert.deepEqual(ruleIds(`KEY=${FIXTURE_SECRET}`, digests), [DIGEST_RULE_ID]);
  assert.deepEqual(ruleIds(`  ${FIXTURE_SECRET},`, digests), [DIGEST_RULE_ID]);
  const [finding] = scanTextForSecrets('x.yaml', `key: '${FIXTURE_SECRET}'`, digests);
  assert.ok(!finding.excerpt.includes(FIXTURE_SECRET));
});

test('a denylisted PEM key matches regardless of line wrapping', () => {
  const body = 'QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo=';
  const digests = new Map([[sha256Hex(normalizePemBody(body)), 'test-fixture']]);
  const wrapped = `${PEM_BEGIN}\r\n${body.slice(0, 10)}\r\n${body.slice(10)}\r\n${PEM_END}\r\n`;
  assert.ok(ruleIds(wrapped, digests).includes(DIGEST_RULE_ID));
});

test('candidate values include quoted literals, tokens and the value side of name=value', () => {
  const candidates = candidateValues('a="first-value" b=second-value third-token-here;');
  for (const expected of ['first-value', 'second-value', 'third-token-here']) assert.ok(candidates.has(expected), expected);
});

test('the committed digest denylist holds only SHA-256 digests and kinds', () => {
  const digests = loadDigestDenylist(DIGEST_FILE);
  assert.ok(digests.size > 0, 'the denylist must not be empty');
  for (const [digest, kind] of digests) {
    assert.match(digest, /^[0-9a-f]{64}$/);
    assert.match(kind, /^[a-z-]+$/);
  }
});

test('CLI exits 1 on a denylisted value and 0 on a clean tree', (testContext) => {
  const directory = makeTempDirectory(testContext);
  writeTree(directory, {
    'digests.json': JSON.stringify({ algorithm: 'sha256', digests: [{ sha256: sha256Hex(FIXTURE_SECRET), kind: 'config-secret' }] }),
    'dirty/appsettings.json': `{ "Signing": { "Key": "${FIXTURE_SECRET}" } }\n`,
    'clean/appsettings.json': '{ "Signing": { "Key": "" } }\n',
  });
  const digestsArg = ['--digests', join(directory, 'digests.json')];
  const failing = runTool('secret-scan.mjs', ['--root', join(directory, 'dirty'), ...digestsArg]);
  assert.equal(failing.status, EXIT_FINDINGS, failing.stderr);
  assert.match(failing.stdout, /known-secret-digest/);
  assert.ok(!failing.stdout.includes(FIXTURE_SECRET), 'the CLI must never print the secret');
  const passing = runTool('secret-scan.mjs', ['--root', join(directory, 'clean'), ...digestsArg]);
  assert.equal(passing.status, EXIT_CLEAN, passing.stdout + passing.stderr);
});

test('the scan does not flag its own tool folder', () => {
  const { findings } = scanTreeForSecrets(REPO_ROOT, loadDigestDenylist(DIGEST_FILE));
  assert.deepEqual(findings.filter((finding) => finding.path.startsWith(OWN_FOLDER)), []);
});
