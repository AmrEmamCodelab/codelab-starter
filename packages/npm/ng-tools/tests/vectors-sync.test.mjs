import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ExitCodes, main, parseArguments } from '../src/vectors/cli.mjs';
import { DriftKinds, compareFolders, listFiles, syncFolders } from '../src/vectors/sync.mjs';

const BIN = fileURLToPath(new URL('../src/bin/codelab-vectors-sync.mjs', import.meta.url));
let root;
let from;
let to;

function write(folder, path, text) {
  mkdirSync(dirname(join(folder, path)), { recursive: true });
  writeFileSync(join(folder, path), text);
}

function silentIo() {
  const io = { logs: [], errors: [] };
  return { io, sink: { log: (line) => io.logs.push(line), error: (line) => io.errors.push(line) } };
}

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ng-tools-vectors-'));
  from = join(root, 'vectors');
  to = join(root, 'consumer', 'vectors');
  write(from, 'canonical.vectors.json', '{"schemaVersion":1,"cases":[]}');
  write(from, 'fixture-keys/test-signing.json', '{"keyId":"t1"}');
});

afterEach(() => rmSync(root, { recursive: true, force: true }));

describe('sync', () => {
  it('lists files recursively with forward slashes, and nothing for an absent folder', () => {
    expect(listFiles(from)).toEqual(['canonical.vectors.json', 'fixture-keys/test-signing.json']);
    expect(listFiles(join(root, 'absent'))).toEqual([]);
  });

  it('copies everything into an empty consumer, after which there is no drift', () => {
    expect(syncFolders({ from, to }).copied).toEqual(['canonical.vectors.json', 'fixture-keys/test-signing.json']);
    expect(compareFolders({ from, to })).toEqual([]);
  });

  it('reports missing, changed and extra files', () => {
    syncFolders({ from, to });
    write(to, 'canonical.vectors.json', '{"schemaVersion":1,"cases":[1]}');
    rmSync(join(to, 'fixture-keys/test-signing.json'));
    write(to, 'stale.vectors.json', '{}');

    expect(compareFolders({ from, to })).toEqual([
      { path: 'canonical.vectors.json', kind: DriftKinds.changed },
      { path: 'fixture-keys/test-signing.json', kind: DriftKinds.missing },
      { path: 'stale.vectors.json', kind: DriftKinds.extra },
    ]);
  });

  it('keeps extra files unless asked to prune', () => {
    write(to, 'local-notes.json', '{}');
    expect(syncFolders({ from, to }).extra).toEqual(['local-notes.json']);
    expect(existsSync(join(to, 'local-notes.json'))).toBe(true);

    expect(syncFolders({ from, to, prune: true }).removed).toEqual(['local-notes.json']);
    expect(existsSync(join(to, 'local-notes.json'))).toBe(false);
  });

  it('refuses a missing source folder', () => {
    expect(() => syncFolders({ from: join(root, 'absent'), to })).toThrow(/not found/);
  });
});

describe('parseArguments', () => {
  it('reads both folders and the flags', () => {
    expect(parseArguments(['--from', 'a', '--to', 'b', '--check'])).toEqual({ options: { from: 'a', to: 'b', check: true, prune: false } });
  });

  it.each([
    [[]],
    [['--from', 'a']],
    [['--from', '--to', 'b']],
    [['--from', 'a', '--to', 'b', '--force']],
    [['--from', 'a', '--to', 'b', '--check', '--prune']],
    [['a', 'b']],
  ])('refuses %j', (argv) => {
    expect(parseArguments(argv).error).toBeDefined();
  });
});

describe('main', () => {
  it('syncs, then --check passes', () => {
    const { io, sink } = silentIo();
    expect(main(['--from', from, '--to', to], sink)).toBe(ExitCodes.ok);
    expect(main(['--from', from, '--to', to, '--check'], sink)).toBe(ExitCodes.ok);
    expect(io.errors).toEqual([]);
  });

  it('--check fails on drift and writes nothing', () => {
    const { io, sink } = silentIo();
    expect(main(['--from', from, '--to', to, '--check'], sink)).toBe(ExitCodes.drift);
    expect(existsSync(to)).toBe(false);
    expect(io.errors.join('\n')).toContain('missing: canonical.vectors.json');
  });

  it('exits with the usage code on bad arguments or a missing source', () => {
    const { sink } = silentIo();
    expect(main(['--to', to], sink)).toBe(ExitCodes.usage);
    expect(main(['--from', join(root, 'absent'), '--to', to], sink)).toBe(ExitCodes.usage);
  });

  it('runs as a bin with the documented exit codes', () => {
    const run = (...argv) => spawnSync(process.execPath, [BIN, ...argv], { encoding: 'utf8' });
    expect(run('--from', from, '--to', to, '--check').status).toBe(ExitCodes.drift);
    expect(run('--from', from, '--to', to).status).toBe(ExitCodes.ok);
    expect(run('--from', from, '--to', to, '--check').status).toBe(ExitCodes.ok);
    expect(readFileSync(join(to, 'canonical.vectors.json'), 'utf8')).toBe('{"schemaVersion":1,"cases":[]}');
    expect(run('--bogus').status).toBe(ExitCodes.usage);
  });
});
