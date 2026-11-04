import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { sha256Hex } from '../lib/hashing.mjs';
import { publicDigestSpellings, PUBLIC_PREIMAGES } from '../lib/public-digests.mjs';
import { loadSecretExceptions, NEVER_EXCEPTABLE, parseSecretExceptions } from '../lib/secret-exceptions.mjs';
import { DIGEST_RULE_ID, scanTreeForSecrets } from '../lib/secret-scanner.mjs';
import { joined, makeTempDirectory, REPO_ROOT, TOOL_DIRECTORY, writeTree } from './helpers.mjs';

const EXCEPTIONS_FILE = join(TOOL_DIRECTORY, 'secret-exceptions.txt');
const NO_DIGESTS = new Map();
const FIXTURE_KEYS = 'packages/vectors/fixture-keys';
// Key-shaped content assembled at run time, so this file is not itself a finding.
const KEY_LINE = joined('{ "material', 'Base64": "', 'q8Vt3LmX0pRw7ZsK2yNb5HcJ4fGd9AeU1iOo6TlQr8M=" }\n');
const VECTOR_LINE = joined('{ "expectedSessionKey', 'Base64": "', 'Zm9vYmFyQmF6UXV4MTIzNDU2Nzg5MGFiY2RlZmdoaWo=" }\n');
const ruleIdsByPath = (findings) => findings.map((finding) => `${finding.path} ${finding.ruleId}`).sort();

test('the checked-in exceptions parse and cover only the fixture keys and the golden vectors', () => {
  const entries = loadSecretExceptions(EXCEPTIONS_FILE);
  assert.ok(entries.length > 0);
  assert.ok(entries.every((entry) => entry.glob.startsWith('packages/vectors/')), 'a secret exception outside packages/vectors');
  assert.ok(entries.every((entry) => !entry.ruleIds.some((ruleId) => NEVER_EXCEPTABLE.includes(ruleId))));
});

test('wildcard rule ids, unanchored globs, unknown rules and the digest rule are refused', () => {
  assert.throws(() => parseSecretExceptions('a/b.json *', 'x'), /exactly/);
  assert.throws(() => parseSecretExceptions('a/b.json high-*', 'x'), /exactly/);
  assert.throws(() => parseSecretExceptions('**/b.json high-entropy-literal', 'x'), /start with a folder/);
  assert.throws(() => parseSecretExceptions('a/b.json not-a-rule', 'x'), /not a secret-scan rule/);
  assert.throws(() => parseSecretExceptions(`a/b.json ${DIGEST_RULE_ID}`, 'x'), /never be excepted/);
  assert.throws(() => parseSecretExceptions('a/b.json unscannable-binary', 'x'), /never be excepted/);
  assert.deepEqual(parseSecretExceptions('# c\na/b.json private-key,jwt\n', 'x'), [{ glob: 'a/b.json', ruleIds: ['private-key', 'jwt'] }]);
});

test('a missing exceptions file means no exceptions', (context) => {
  assert.deepEqual(loadSecretExceptions(join(makeTempDirectory(context), 'absent.txt')), []);
});

test('the same key-shaped content is excepted only where the fixture keys and vectors live', (context) => {
  const root = makeTempDirectory(context);
  writeTree(root, {
    [`${FIXTURE_KEYS}/signing.json`]: KEY_LINE,
    'packages/vectors/session-keys.vectors.json': VECTOR_LINE,
    'packages/other/signing.json': KEY_LINE,
    'src/session-keys.vectors.json': VECTOR_LINE,
    'packages/vectors/nested/session-keys.vectors.json': VECTOR_LINE,
  });
  const exceptions = loadSecretExceptions(EXCEPTIONS_FILE);

  const all = scanTreeForSecrets(root, NO_DIGESTS).findings;
  const remaining = scanTreeForSecrets(root, NO_DIGESTS, exceptions).findings;

  assert.ok(all.some((finding) => finding.path.startsWith(FIXTURE_KEYS)), 'the content must be a finding without the exception');
  assert.deepEqual(new Set(remaining.map((finding) => finding.path)), new Set([
    'packages/other/signing.json', 'src/session-keys.vectors.json', 'packages/vectors/nested/session-keys.vectors.json',
  ]));
});

test('the checked-in fixture keys and vectors are findings anywhere else in the repository', (context) => {
  const root = makeTempDirectory(context);
  const signing = readFileSync(join(REPO_ROOT, FIXTURE_KEYS, 'signing.json'), 'utf8');
  const vectors = readFileSync(join(REPO_ROOT, 'packages/vectors/session-keys.vectors.json'), 'utf8');
  writeTree(root, { [`${FIXTURE_KEYS}/signing.json`]: signing, 'docs/signing.json': signing, 'docs/session-keys.json': vectors });

  const paths = new Set(scanTreeForSecrets(root, NO_DIGESTS, loadSecretExceptions(EXCEPTIONS_FILE)).findings.map((finding) => finding.path));

  assert.deepEqual(paths, new Set(['docs/signing.json', 'docs/session-keys.json']));
});

test('a known source-product digest is reported even inside an excepted folder', (context) => {
  const root = makeTempDirectory(context);
  const known = joined('Kp4', '-known-harvest-', 'fixture-9Qz');
  writeTree(root, { [`${FIXTURE_KEYS}/signing.json`]: `{ "keyId": "${known}" }\n` });

  const { findings } = scanTreeForSecrets(root, new Map([[sha256Hex(known), 'test-fixture']]), loadSecretExceptions(EXCEPTIONS_FILE));

  assert.deepEqual(ruleIdsByPath(findings), [`${FIXTURE_KEYS}/signing.json ${DIGEST_RULE_ID}`]);
});

test('the SHA-256 of a public input is never a known-secret finding; other listed values still are', (context) => {
  const root = makeTempDirectory(context);
  const [emptyBodyHash] = [...publicDigestSpellings([''])].filter((spelling) => spelling.length === 43);
  const listed = joined('Lq7', '-listed-', 'value-3Rk');
  writeTree(root, { 'a.json': `{ "bodyHash": "${emptyBodyHash}", "other": "${listed}" }\n` });
  const digests = new Map([[sha256Hex(emptyBodyHash), 'test-fixture'], [sha256Hex(listed), 'test-fixture']]);

  const { findings } = scanTreeForSecrets(root, digests);

  const digestFindings = findings.filter((finding) => finding.ruleId === DIGEST_RULE_ID);
  assert.deepEqual(digestFindings.map((finding) => finding.excerpt), [`${listed.slice(0, 4)}****`]);
});

test('public digest spellings cover hex, base64 and base64url of each preimage', () => {
  const spellings = publicDigestSpellings();
  assert.ok(spellings.has(sha256Hex('')));
  assert.ok(spellings.has(sha256Hex('').toUpperCase()));
  assert.ok(spellings.has(Buffer.from(sha256Hex('{}'), 'hex').toString('base64url')));
  assert.equal(spellings.size, PUBLIC_PREIMAGES.length * 5);
});
