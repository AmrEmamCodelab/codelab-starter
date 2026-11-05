// Unit tests of the hash normalization, the product prefix and the CLI argument parser.
// Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { after, describe, it } from 'node:test';
import { OptionKind, parseOptions, UsageError } from '../lib/args.mjs';
import { computeHashes, hashContent, isBinary, isHashExcluded, normalizeLineEndings } from '../lib/hashing.mjs';
import { refusalForPrefix, setAtPath, setJsonValue, splitJsonPath } from '../lib/prefix.mjs';
import { makeTempDir } from './support/powershell.mjs';
import { write } from './support/synthetic-starter.mjs';

const sha256 = (text) => createHash('sha256').update(text).digest('hex');

describe('hash normalization', () => {
  it('hashes CRLF and LF checkouts of the same text identically', () => {
    assert.equal(hashContent(Buffer.from('a\r\nb\r\n')), sha256('a\nb\n'));
    assert.equal(hashContent(Buffer.from('a\nb\n')), sha256('a\nb\n'));
  });

  it('keeps lone CRs and hashes binary content unchanged', () => {
    assert.equal(normalizeLineEndings(Buffer.from('a\rb\r\n')).toString(), 'a\rb\n');
    const binary = Buffer.from([0x00, 0x0d, 0x0a, 0x01]);
    assert.equal(isBinary(binary), true);
    assert.equal(hashContent(binary), createHash('sha256').update(binary).digest('hex'));
  });

  it('returns the same buffer when there is nothing to normalize', () => {
    const buffer = Buffer.from('plain\n');
    assert.equal(normalizeLineEndings(buffer), buffer);
  });

  it('excludes the manifest and the feed, and sorts the paths', () => {
    assert.equal(isHashExcluded('codelab.starter.json'), true);
    assert.equal(isHashExcluded('feed/nuget/x.nupkg'), true);
    assert.equal(isHashExcluded('feedback.md'), false);
    const temp = makeTempDir();
    try {
      write(temp.path, 'b.md', 'b\r\n');
      write(temp.path, 'a.md', 'a\n');
      write(temp.path, 'codelab.starter.json', '{}');
      const hashes = computeHashes(temp.path, ['b.md', 'codelab.starter.json', 'a.md']);
      assert.deepEqual(Object.keys(hashes), ['a.md', 'b.md']);
      assert.equal(hashes['b.md'], sha256('b\n'));
    } finally {
      temp.cleanup();
    }
  });
});

describe('product prefix', () => {
  it('accepts 2-16 characters, A-Z first, then A-Z or 0-9', () => {
    for (const prefix of ['AB', 'ACME', 'MOH2', 'A'.repeat(16)]) assert.equal(refusalForPrefix(prefix), null, prefix);
  });

  it('refuses invalid and reserved prefixes', () => {
    for (const prefix of ['A', 'acme', '2ACME', 'AC-ME', 'A'.repeat(17), undefined, '']) assert.match(refusalForPrefix(prefix), /invalid/, String(prefix));
    for (const prefix of ['STARTER', 'CODELABTEST', 'ALTPFX']) assert.match(refusalForPrefix(prefix), /reserved/);
  });

  it('splits .NET and JSON paths', () => {
    assert.deepEqual(splitJsonPath('Codelab:Security:Protocol:Prefix'), ['Codelab', 'Security', 'Protocol', 'Prefix']);
    assert.deepEqual(splitJsonPath('security.protocolPrefix'), ['security', 'protocolPrefix']);
  });

  it('sets a value keeping indentation, CRLF, BOM and the final newline', () => {
    const text = '﻿{\r\n    "Codelab": {\r\n        "Security": { "Protocol": { "Prefix": "STARTER" } }\r\n    }\r\n}\r\n';
    const result = setJsonValue(text, 'Codelab:Security:Protocol:Prefix', 'ACME');
    assert.equal(result.previous, 'STARTER');
    assert.equal(result.created, false);
    assert.ok(result.text.startsWith('﻿{\r\n    "Codelab": {\r\n        "Security": {'));
    assert.ok(result.text.endsWith('}\r\n'));
    assert.equal(JSON.parse(result.text.slice(1)).Codelab.Security.Protocol.Prefix, 'ACME');
  });

  it('creates missing objects and reports it', () => {
    const result = setJsonValue('{"a":1}', 'security.protocolPrefix', 'ACME');
    assert.equal(result.created, true);
    assert.deepEqual(JSON.parse(result.text), { a: 1, security: { protocolPrefix: 'ACME' } });
  });

  it('refuses to cross a non-object and refuses JSON with comments', () => {
    assert.throws(() => setAtPath({ a: 'x' }, ['a', 'b'], 'v'), /not an object/);
    assert.throws(() => setJsonValue('{ // c\n}', 'a', 'v'));
  });
});

describe('parseOptions', () => {
  const spec = { '--root': OptionKind.string, '--minimal': OptionKind.boolean, '--remove-module': OptionKind.list };

  it('parses strings, switches and repeated lists into camelCase keys', () => {
    assert.deepEqual(parseOptions(['--root', 'x', '--minimal', '--remove-module', 'A', '--remove-module', 'B'], spec), { root: 'x', minimal: true, removeModule: ['A', 'B'] });
    assert.deepEqual(parseOptions([], spec), { root: undefined, minimal: false, removeModule: [] });
  });

  it('refuses unknown flags, missing values and missing required options', () => {
    assert.throws(() => parseOptions(['--nope'], spec), UsageError);
    assert.throws(() => parseOptions(['--root'], spec), /needs a value/);
    assert.throws(() => parseOptions(['--root', '--minimal'], spec), /needs a value/);
    assert.throws(() => parseOptions([], spec, ['--root']), /--root is required/);
  });
});

describe('temp cleanup', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());
  it('makeTempDir gives a writable folder', () => {
    assert.ok(write(temp.path, 'x.txt', 'x'));
  });
});
