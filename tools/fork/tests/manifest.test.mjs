// Unit tests of codelab.starter.json: the repo's own template, the JSON schema and the hand validation agree.
// Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';
import { Ajv2020 } from 'ajv/dist/2020.js';
import { formatManifest, ManifestError, MANIFEST_FIELDS, readManifest, renameMapErrors, updateManifest, validateManifest, writeManifest } from '../lib/manifest.mjs';
import { makeTempDir } from './support/powershell.mjs';
import { REPO_ROOT, write } from './support/synthetic-starter.mjs';

const schema = JSON.parse(readFileSync(join(REPO_ROOT, 'tools/fork/codelab.starter.schema.json'), 'utf8'));
const validateWithSchema = new Ajv2020({ allErrors: true, strict: true }).compile(schema);
const template = () => JSON.parse(readFileSync(join(REPO_ROOT, 'codelab.starter.json'), 'utf8'));
const COMMIT = 'f'.repeat(40);
const HASH = 'e'.repeat(64);

const VALID_FORK = () => ({
  ...template(),
  productPrefix: 'ACME',
  starterBase: COMMIT,
  renameMap: [{ from: 'Starter', to: 'Acme' }],
  removedModules: ['Demo'],
  hashes: { 'docs/one.md': HASH },
});

/** Each case breaks one rule; both validators must reject it. */
const INVALID = Object.freeze({
  'missing field': (manifest) => { delete manifest.hashes; },
  'unknown field': (manifest) => { manifest.extra = true; },
  'schema version': (manifest) => { manifest.schemaVersion = 2; },
  'starter version': (manifest) => { manifest.starterVersion = 'v1'; },
  'lower-case prefix': (manifest) => { manifest.productPrefix = 'acme'; },
  'long prefix': (manifest) => { manifest.productPrefix = 'A'.repeat(17); },
  'product name without ar': (manifest) => { manifest.productName = { en: 'X' }; },
  'short starterBase': (manifest) => { manifest.starterBase = 'abc123'; },
  'rename entry without to': (manifest) => { manifest.renameMap = [{ from: 'Starter' }]; },
  'empty rename from': (manifest) => { manifest.renameMap = [{ from: '', to: 'Acme' }]; },
  'module name': (manifest) => { manifest.removedModules = ['demo-module']; },
  'duplicate modules': (manifest) => { manifest.requiredModules = ['Identity', 'Identity']; },
  'prefix target not json': (manifest) => { manifest.prefixTargets = [{ file: 'a.yaml', jsonPath: 'x' }]; },
  'prefix target path': (manifest) => { manifest.prefixTargets = [{ file: 'a.json', jsonPath: 'a::b' }]; },
  'hash value': (manifest) => { manifest.hashes = { 'a.md': 'xyz' }; },
  'backslash hash key': (manifest) => { manifest.hashes = { 'docs\\a.md': HASH }; },
});

describe('the starter template codelab.starter.json', () => {
  it('is valid against the schema and the hand validation', () => {
    assert.equal(validateWithSchema(template()), true, JSON.stringify(validateWithSchema.errors));
    assert.deepEqual(validateManifest(template()), []);
  });

  it('is the starter itself: no starterBase, no rename, nothing removed, Identity and Access required', () => {
    const manifest = template();
    assert.equal(manifest.starterBase, null);
    assert.deepEqual(manifest.renameMap, []);
    assert.deepEqual(manifest.removedModules, []);
    assert.deepEqual(manifest.requiredModules, ['Identity', 'Access']);
    assert.equal(manifest.productPrefix, 'STARTER');
    assert.deepEqual(manifest.prefixTargets, [
      { file: 'backend/src/Starter.Api/appsettings.json', jsonPath: 'Codelab:Security:Protocol:Prefix' },
      { file: 'frontend/starter-web/public/config.json', jsonPath: 'security.protocolPrefix' },
    ]);
  });

  it('is written in the canonical format', () => {
    assert.equal(readFileSync(join(REPO_ROOT, 'codelab.starter.json'), 'utf8'), formatManifest(template()));
  });

  it('lists the schema required fields in the written field order', () => {
    assert.deepEqual(schema.required, [...MANIFEST_FIELDS]);
  });
});

describe('validateManifest agrees with the schema', () => {
  it('accepts an initialized fork', () => {
    assert.equal(validateWithSchema(VALID_FORK()), true, JSON.stringify(validateWithSchema.errors));
    assert.deepEqual(validateManifest(VALID_FORK()), []);
  });

  for (const [name, breakIt] of Object.entries(INVALID)) {
    it(`rejects: ${name}`, () => {
      const manifest = VALID_FORK();
      breakIt(manifest);
      assert.equal(validateWithSchema(manifest), false, `schema accepted ${name}`);
      assert.notDeepEqual(validateManifest(manifest), [], `validateManifest accepted ${name}`);
    });
  }

  it('rejects a non-object and reports renameMap errors on their own', () => {
    assert.notDeepEqual(validateManifest([]), []);
    assert.deepEqual(renameMapErrors([{ from: 'A', to: 'B' }]), []);
    assert.equal(renameMapErrors('x').length, 1);
  });
});

describe('read, write and update', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());

  it('writes sorted hashes and reads them back', () => {
    const manifest = { ...VALID_FORK(), hashes: { 'z.md': HASH, 'a.md': HASH } };
    writeManifest(temp.path, manifest);
    const text = readFileSync(join(temp.path, 'codelab.starter.json'), 'utf8');
    assert.ok(text.indexOf('"a.md"') < text.indexOf('"z.md"'));
    assert.deepEqual(readManifest(temp.path).hashes, { 'a.md': HASH, 'z.md': HASH });
  });

  it('updateManifest applies a mutation and validates it', () => {
    updateManifest(temp.path, (draft) => {
      draft.removedModules.push('Other');
    });
    assert.deepEqual(readManifest(temp.path).removedModules, ['Demo', 'Other']);
    assert.throws(() => updateManifest(temp.path, (draft) => {
      draft.productPrefix = 'bad';
    }), ManifestError);
    assert.equal(readManifest(temp.path).productPrefix, 'ACME');
  });

  it('refuses a missing, malformed or invalid manifest', () => {
    const empty = makeTempDir();
    try {
      assert.throws(() => readManifest(empty.path), /not found/);
      write(empty.path, 'codelab.starter.json', '{ nope');
      assert.throws(() => readManifest(empty.path), /not valid JSON/);
      write(empty.path, 'codelab.starter.json', '{}');
      assert.throws(() => readManifest(empty.path), /is invalid/);
      write(empty.path, 'codelab.starter.json', `﻿${formatManifest(VALID_FORK())}`);
      assert.equal(readManifest(empty.path).productPrefix, 'ACME');
    } finally {
      empty.cleanup();
    }
  });
});
