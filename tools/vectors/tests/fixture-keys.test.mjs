import assert from 'node:assert/strict';
import { createHash, createPrivateKey, createPublicKey } from 'node:crypto';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { digestsOf } from '../../extract/lib/key-material.mjs';
import {
  FIXTURE_FILES, fixtureKeyValues, generateFixtureDocuments, pemSpellings, readFixtureDocuments, SYMMETRIC_KEY_BYTES,
  writeFixtureDocuments,
} from '../lib/fixture-keys.mjs';
import {
  entriesForValues, entryFor, FIXTURE_KEYS_PATH, isPublishedFixturesFileCurrent, PUBLISHED_FIXTURES_PATH,
  publishedFixturesText,
} from '../lib/published-fixtures.mjs';

const REPO_ROOT = resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const counterBytes = () => {
  let next = 0;
  return (size) => Buffer.from(Array.from({ length: size }, () => (next += 1) % 256));
};

function tempDirectory(context) {
  const directory = mkdtempSync(join(tmpdir(), 'codelab-fixture-keys-test-'));
  context.after(() => rmSync(directory, { recursive: true, force: true }));
  return directory;
}

test('a generated set has every file, ring and key, labelled TEST-ONLY', () => {
  const documents = generateFixtureDocuments(counterBytes());
  for (const { file, kind, keyIds } of FIXTURE_FILES) {
    const document = documents[file];
    assert.equal(document.testOnly, true);
    assert.equal(document.kind, kind);
    const ids = document.ring ? document.ring.keys.map((key) => key.keyId) : [document.keyId];
    assert.deepEqual(ids, keyIds);
  }
  const material = Buffer.from(documents['signing.json'].ring.keys[0].materialBase64, 'base64');
  assert.equal(material.length, SYMMETRIC_KEY_BYTES);
  // The armour line is assembled so this file carries no private-key marker of its own.
  assert.ok(documents['service-assertion.json'].privateKeyPem.startsWith(['-----BEGIN', 'PRIVATE', 'KEY-----'].join(' ')));
});

test('writing refuses to replace existing keys unless forced, and the files read back', (context) => {
  const directory = tempDirectory(context);
  writeFixtureDocuments(directory, generateFixtureDocuments());
  assert.throws(() => writeFixtureDocuments(directory, generateFixtureDocuments()), /--force/);
  const replacement = generateFixtureDocuments();
  writeFixtureDocuments(directory, replacement, { force: true });
  assert.deepEqual(readFixtureDocuments(directory), replacement);
});

test('reading refuses a missing file and a file without the TEST-ONLY label', (context) => {
  const directory = tempDirectory(context);
  assert.throws(() => readFixtureDocuments(directory), /MISSING fixture key file/);
  const documents = generateFixtureDocuments();
  documents['public-id.json'].testOnly = false;
  writeFixtureDocuments(directory, documents);
  assert.throws(() => readFixtureDocuments(directory), /not labelled TEST-ONLY/);
});

test('a PEM is listed as written, without its final newline, with CRLF and as its bare body', () => {
  const pem = '-----BEGIN PUBLIC KEY-----\nAAAA\nBBBB\n-----END PUBLIC KEY-----\n';
  assert.deepEqual(pemSpellings(pem), [
    pem,
    pem.trimEnd(),
    pem.replaceAll('\n', '\r\n'),
    pem.replaceAll('\n', '\r\n').trimEnd(),
    'AAAABBBB',
  ]);
});

test('a denylist entry is the SHA-256 of the key digest bytes, sorted and distinct', () => {
  const digest = createHash('sha256').update('k').digest('hex');
  assert.equal(entryFor(digest), createHash('sha256').update(Buffer.from(digest, 'hex')).digest('hex'));
  const value = Buffer.alloc(SYMMETRIC_KEY_BYTES, 7).toString('base64');
  const entries = entriesForValues([value, value]);
  assert.deepEqual(entries, [...new Set(digestsOf(value).map(entryFor))].sort());
  assert.equal(entries.length, 2, 'the text digest and the decoded-bytes digest');
});

test('the checked-in fixture keys are labelled and the checked-in denylist file is current', () => {
  const documents = readFixtureDocuments(join(REPO_ROOT, FIXTURE_KEYS_PATH));
  assert.ok(isPublishedFixturesFileCurrent(join(REPO_ROOT, PUBLISHED_FIXTURES_PATH), documents),
    'published-material.fixtures.json is stale: run node tools/vectors/fixture-keys.mjs published-material');
  assert.equal(isPublishedFixturesFileCurrent(join(REPO_ROOT, 'absent.json'), documents), false);
  assert.match(publishedFixturesText(documents), /"keyDigestHashes": \[/);
});

test('the checked-in fixture keys are fresh: none is harvest-source key material', () => {
  const harvest = new Set(JSON.parse(readFileSync(join(REPO_ROOT, 'tools/extract/secret-digests.json'), 'utf8')).digests.map((entry) => entry.sha256));
  const documents = readFixtureDocuments(join(REPO_ROOT, FIXTURE_KEYS_PATH));
  for (const value of fixtureKeyValues(documents)) {
    assert.ok(digestsOf(value).every((digest) => !harvest.has(digest)), 'a fixture key matches the harvest denylist');
  }
});

test('the denylist holds the DER SubjectPublicKeyInfo digest of the fixture key pair (what Codelab.Security matches for any PEM spelling)', () => {
  const documents = readFixtureDocuments(join(REPO_ROOT, FIXTURE_KEYS_PATH));
  const entries = new Set(entriesForValues(fixtureKeyValues(documents)));
  const { privateKeyPem } = documents['service-assertion.json'];
  const publicKeyInfo = createPublicKey(createPrivateKey(privateKeyPem)).export({ type: 'spki', format: 'der' });
  assert.ok(entries.has(entryFor(createHash('sha256').update(publicKeyInfo).digest('hex'))),
    'the SubjectPublicKeyInfo digest is missing: pemSpellings must keep the bare base64 body of the public key PEM');
});
