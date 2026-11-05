// Unit tests of the working-tree rename and the batch blob access. Run: node --test "tools/fork/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';
import { readBlobs, writeBlobs } from '../lib/blobs.mjs';
import { createRenamer } from '../lib/rename.mjs';
import { renameTree } from '../lib/rename-tree.mjs';
import { makeTempDir } from './support/powershell.mjs';
import { git, write } from './support/synthetic-starter.mjs';

describe('renameTree', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());

  it('renames text contents and paths, removes emptied folders, skips binaries and the manifest', () => {
    write(temp.path, 'backend/src/Starter.Api/Starter.Api.csproj', '<RootNamespace>Starter.Api</RootNamespace>\r\n');
    write(temp.path, 'assets/logo.bin', Buffer.from([0x00, 0x53, 0x74, 0x61, 0x72, 0x74, 0x65, 0x72]));
    write(temp.path, 'codelab.starter.json', '{"renameMap":[{"from":"Starter","to":"Acme"}]}');
    write(temp.path, 'docs/plain.md', 'nothing to rename\n');
    const files = ['backend/src/Starter.Api/Starter.Api.csproj', 'assets/logo.bin', 'codelab.starter.json', 'docs/plain.md'];
    const result = renameTree(temp.path, files, createRenamer([{ from: 'Starter', to: 'Acme' }]));
    assert.deepEqual(result.moves, [{ from: 'backend/src/Starter.Api/Starter.Api.csproj', to: 'backend/src/Acme.Api/Acme.Api.csproj' }]);
    assert.deepEqual(result.contents, ['backend/src/Starter.Api/Starter.Api.csproj']);
    assert.equal(readFileSync(join(temp.path, 'backend/src/Acme.Api/Acme.Api.csproj'), 'utf8'), '<RootNamespace>Acme.Api</RootNamespace>\r\n');
    assert.equal(existsSync(join(temp.path, 'backend/src/Starter.Api')), false);
    assert.ok(existsSync(join(temp.path, 'backend/src')));
    assert.match(readFileSync(join(temp.path, 'codelab.starter.json'), 'utf8'), /"from":"Starter"/);
    assert.deepEqual([...readFileSync(join(temp.path, 'assets/logo.bin'))].slice(0, 2), [0x00, 0x53]);
  });

  it('does nothing for the identity map', () => {
    assert.deepEqual(renameTree(temp.path, ['docs/plain.md'], createRenamer([])), { contents: [], moves: [] });
  });
});

describe('blobs', () => {
  const temp = makeTempDir();
  after(() => temp.cleanup());

  it('writes blobs without filters and reads them back in one batch, skipping unknown ids', () => {
    git(temp.path, ['init', '--quiet']);
    const contents = [Buffer.from('line\r\n'), Buffer.from(''), Buffer.from('قالب\n')];
    const ids = writeBlobs(temp.path, contents);
    assert.equal(ids.length, 3);
    assert.equal(ids[1], 'e69de29bb2d1d6434b8b29ae775ad8c2e48c5391');
    const missing = 'f'.repeat(40);
    const blobs = readBlobs(temp.path, [...ids, missing, ids[0]]);
    assert.deepEqual(ids.map((id) => blobs.get(id).toString('utf8')), ['line\r\n', '', 'قالب\n']);
    assert.equal(blobs.has(missing), false);
    assert.deepEqual(writeBlobs(temp.path, []), []);
    assert.equal(readBlobs(temp.path, []).size, 0);
  });
});
