// Regression tests: credential shapes the secret scan used to miss. Every secret-shaped input is assembled at run
// time with joined(...) so this file never contains one in scannable form.
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DIGEST_RULE_ID, scanTextForSecrets } from '../lib/secret-scanner.mjs';
import { digestsOf } from '../lib/key-material.mjs';
import { KIND_SCRIPT_SECRET, secretsFromScript } from '../lib/digest-sources.mjs';
import { entropyBitsPerChar, looksLikeKeyMaterial } from '../lib/secret-rules.mjs';
import { joined } from './helpers.mjs';

const NO_DIGESTS = new Map();
// 48-char base64 (36 bytes, valid, no padding) and 64-char hex: the shapes of HMAC and AES keys.
const BASE64_KEY = joined('k7Qw2Lm9Xp4Rt8Vz', 'Bc3Nf6Hj1Ds5Ga0K', 'Yu2Io9Pl4Mn7Wq3A');
const HEX_KEY = joined('3c7e1a9b5d2f8e40', '6b1d9c3a7e5f2b80', 'c4a8e2f6b0d3971e', '5a2c8e4b6d0f1397');
const PASSPHRASE = joined('correcthorse', 'batterystaple');

const ruleIds = (path, text) => scanTextForSecrets(path, text, NO_DIGESTS).map((finding) => finding.ruleId);
const caught = (path, text) => ruleIds(path, text).length > 0;

test('regression: key material under a generic Key/Token/HashKey name is caught (JSON, any file)', () => {
  for (const name of ['Key', 'HashKey', 'TicketKey', 'UserIdHashKey', 'KeyBase64', 'Token', 'Hmac']) {
    assert.ok(caught('appsettings.Production.json', `"${name}": "${BASE64_KEY}"`), name);
  }
  assert.ok(caught('appsettings.json', joined('{"PublicId":{"Key":"', BASE64_KEY, '"}}')));
  assert.ok(caught('appsettings.json', `"KeyHex": "${HEX_KEY}"`));
  assert.ok(caught('appsettings.json', `"KeyHex": "${HEX_KEY.toUpperCase()}"`));
});

test('regression: unquoted secrets in env, YAML and PowerShell files are caught', () => {
  assert.ok(caught('.env', joined('DB_PASS', 'WORD=', 'Str0ng!Value9')));
  assert.ok(caught('docker-compose.yaml', joined('      MSSQL_SA_PASS', 'WORD: ', 'Str0ng!Value9')));
  assert.ok(caught('config.yml', joined('password: ', 'Str0ng!Value9')));
  assert.ok(caught('deploy.ps1', joined('$SaPwd = ', 'Str0ng!Value9')));
});

test('regression: quoted passphrases under password/secret names are caught; names of settings are not', () => {
  assert.ok(caught('a.json', joined('"Pass', 'word": "', PASSPHRASE, '"')));
  assert.ok(caught('a.json', joined('"Client', 'Secret": "', 'blue-river-stone-cloud', '"')));
  assert.ok(caught('a.ps1', joined("param([string]$SaPwd = '", 'Str0ng!Value9', "')")));
  assert.deepEqual(ruleIds('a.mjs', joined("const KIND_CONFIG_", "SECRET = 'config-secret';")), []);
  assert.deepEqual(ruleIds('a.ts', joined("const csrfTok", "en = 'X-XSRF-TOKEN';")), []);
});

test('regression: bearer tokens, RSA XML and JWK private parts, URL credentials are caught', () => {
  assert.ok(caught('a.http', joined('Authorization: Bear', 'er ', BASE64_KEY)));
  assert.ok(caught('key.xml', joined('<RSAKeyValue><D>', BASE64_KEY, '</D></RSAKeyValue>')));
  assert.ok(caught('jwk.json', joined('{"kty":"RSA","d":"', BASE64_KEY.replace(/[+/=]/g, ''), '"}')));
  assert.ok(caught('a.md', joined('https://admin:', 'Str0ng!Value9', '@build.example.test/')));
  assert.deepEqual(ruleIds('a.md', 'https://user:${PASSWORD}@build.example.test/'), []);
});

test('regression: command-line and PowerShell plaintext passwords, PuTTY keys and SAS signatures are caught', () => {
  assert.ok(caught('a.ps1', joined('sqlcmd -S localhost -U sa -', 'P Str0ng!Value9 -Q "select 1"')));
  assert.ok(caught('a.ps1', joined("ConvertTo-SecureString '", 'Str0ng!Value9', "' -AsPlainText -Force")));
  assert.ok(caught('k.ppk', joined('PuTTY-User-', 'Key-File-3: ssh-rsa')));
  assert.ok(caught('a.md', joined('https://acct.blob.example.test/c?sv=2024&', 'sig=', BASE64_KEY)));
});

test('regression: a high-entropy quoted literal is caught; hashes and integrity strings are not', () => {
  assert.ok(caught('a.cs', joined('var k = "', BASE64_KEY.replace(/=+$/, ''), 'Zx8Qp3Lr', '";')));
  assert.deepEqual(ruleIds('a.json', `"commit": "${HEX_KEY}"`), []);
  assert.deepEqual(ruleIds('package-lock.json', joined('"integrity": "sha512-', BASE64_KEY, BASE64_KEY, '"')), []);
});

test('key-material and entropy helpers', () => {
  assert.ok(looksLikeKeyMaterial(BASE64_KEY));
  assert.ok(looksLikeKeyMaterial(HEX_KEY));
  assert.ok(!looksLikeKeyMaterial('PermissionsManagementDialogComponentName'));
  assert.ok(entropyBitsPerChar(BASE64_KEY) > entropyBitsPerChar('aaaaaaaaaaaaaaaaaaaaaaaa'));
});

test('regression: a denylisted key is found re-encoded (upper-case hex, base64url, hex of the bytes) or split in two', () => {
  const bytes = Buffer.from(BASE64_KEY, 'base64');
  const digests = new Map(digestsOf(BASE64_KEY).map((digest) => [digest, 'vector-key']));
  for (const text of [
    `k = "${bytes.toString('hex').toUpperCase()}"`,
    `k = "${bytes.toString('base64url')}"`,
    `k = "${bytes.toString('hex')}"`,
    `k = "${BASE64_KEY.slice(0, 20)}" + "${BASE64_KEY.slice(20)}"`,
  ]) {
    const ids = scanTextForSecrets('x.cs', text, digests).map((finding) => finding.ruleId);
    assert.ok(ids.includes(DIGEST_RULE_ID), text.slice(0, 12));
  }
});

test('regression: digest sources include unquoted and command-line secrets from compose files, workflows and scripts', () => {
  const literal = joined('Str0ng', '!Value9');
  const values = (text) => secretsFromScript(text).map((entry) => entry.value);
  assert.ok(values(joined('      MSSQL_SA_PASS', 'WORD: ', literal)).includes(literal));
  assert.ok(values(joined('sqlcmd -S db -U sa -', 'P ', literal)).includes(literal));
  assert.ok(values(joined('SA_PASS', 'WORD=', literal)).includes(literal));
  assert.ok(secretsFromScript(joined('SA_PASS', 'WORD=', literal)).every((entry) => entry.kind === KIND_SCRIPT_SECRET));
});
