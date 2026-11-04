import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  digestInputFor, KIND_CONFIG_SECRET, KIND_CONNECTION_STRING, KIND_TEST_FIXTURE, KIND_VECTOR_KEY, parseLenientJson,
  secretsFromConfig, secretsFromFixtureCode, secretsFromVectors,
} from '../lib/digest-sources.mjs';
import { normalizePemBody } from '../lib/secret-scanner.mjs';
import { joined } from './helpers.mjs';

const BYTE_ORDER_MARK = '﻿';

test('lenient JSON accepts a BOM, comments and trailing commas, and keeps // inside strings', () => {
  const parsed = parseLenientJson(`${BYTE_ORDER_MARK}{ // note\n "a": "http://x", /* block */ "b": [1, 2,], }`);
  assert.deepEqual(parsed, { a: 'http://x', b: [1, 2] });
});

test('config extraction takes secret-named settings and every connection string, nothing else', () => {
  const config = JSON.stringify({
    ConnectionStrings: { Main: 'Server=db;Database=app;Trusted_Connection=True' },
    Jwt: { SecretKey: 'a-long-dev-secret-value', Issuer: 'issuer-name-value' },
    Gateway: { ApiKeyHeaderName: 'x-gateway-header', ApiKey: 'gateway-key-value' },
    '//SecretKey': 'documentation prose, not a secret',
    Short: { Password: 'short' },
  });
  const found = secretsFromConfig(config);
  assert.deepEqual(found, [
    { value: 'Server=db;Database=app;Trusted_Connection=True', kind: KIND_CONNECTION_STRING },
    { value: 'a-long-dev-secret-value', kind: KIND_CONFIG_SECRET },
    { value: 'gateway-key-value', kind: KIND_CONFIG_SECRET },
  ]);
});

test('vector extraction takes key-material fields only', () => {
  const vectors = JSON.stringify({ cases: [{ masterBase64: 'QEFCQ0RFRkdISUpL', keyId: 's1', mac: 'abcdefghij', sessionKeyHex: '0011223344556677' }] });
  assert.deepEqual(secretsFromVectors(vectors).map((entry) => entry.value), ['QEFCQ0RFRkdISUpL', '0011223344556677']);
  assert.ok(secretsFromVectors(vectors).every((entry) => entry.kind === KIND_VECTOR_KEY));
});

test('fixture extraction takes key-looking literals, not words or media types', () => {
  const code = [
    `var key = "${joined('q83vEjRWeJq83vEjRWeJ', 'q83vEjRWeJq83vEjRWeJAAA=')}";`,
    'var hex = "00112233445566778899aabbccddeeff";',
    'var media = "application/problem+json-content-type";',
    'var name = "PermissionsManagementDialogComponent";',
  ].join('\n');
  const values = secretsFromFixtureCode(code).map((entry) => entry.value);
  assert.deepEqual(values, ['q83vEjRWeJq83vEjRWeJq83vEjRWeJq83vEjRWeJAAA=', '00112233445566778899aabbccddeeff']);
  assert.ok(secretsFromFixtureCode(code).every((entry) => entry.kind === KIND_TEST_FIXTURE));
});

test('a PEM value is digested by its normalised body, so the scanner can match it', () => {
  const pem = joined('-----BEGIN', ' PRIVATE KEY-----\nAAAA\nBBBB\n-----END', ' PRIVATE KEY-----');
  assert.equal(digestInputFor(pem), normalizePemBody('\nAAAA\nBBBB\n'));
  assert.equal(digestInputFor('plain-value'), 'plain-value');
});
