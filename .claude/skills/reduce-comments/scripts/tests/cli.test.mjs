import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { ExitCode, parseArguments, resolveDialect, run } from '../lib/cli.mjs';
import { Dialect } from '../lib/dialects.mjs';
import { decodeSource } from '../lib/source-file.mjs';

const SCRIPT = join(dirname(fileURLToPath(import.meta.url)), '..', 'comment-diff.mjs');

function runWith(args, files) {
  const out = [];
  const err = [];
  const code = run(args, {
    out: (line) => out.push(line),
    err: (line) => err.push(line),
    read: (path) => {
      if (!(path in files)) {
        throw new Error(`cannot read ${path}`);
      }
      return files[path];
    },
  });
  return { code, out: out.join('\n'), err: err.join('\n') };
}

test('verify exits 0 for a comments-only change and 1 for a code change', () => {
  const files = { 'a.cs': 'int a; // c\n', 'b.cs': 'int a;\n', 'c.cs': 'int b;\n' };
  assert.equal(runWith(['verify', 'a.cs', 'b.cs'], files).code, ExitCode.COMMENTS_ONLY);
  const failed = runWith(['verify', 'a.cs', 'c.cs'], files);
  assert.equal(failed.code, ExitCode.CODE_CHANGED);
  assert.match(failed.out, /^FAIL/);
});

test('verify takes the dialect from the edited file, so a baseline copy may have any extension', () => {
  const files = { 'baseline.bak': '-- c\nSELECT 1', 'q.sql': 'SELECT 1' };
  assert.equal(runWith(['verify', 'baseline.bak', 'q.sql'], files).code, ExitCode.COMMENTS_ONLY);
});

test('unsupported files, unreadable files and bad arguments exit 2', () => {
  const files = { 'a.txt': 'x', 'b.txt': 'x', 'a.cs': 'int a;' };
  assert.equal(runWith(['verify', 'a.txt', 'b.txt'], files).code, ExitCode.USAGE);
  assert.equal(runWith(['verify', 'a.cs', 'missing.cs'], files).code, ExitCode.USAGE);
  assert.equal(runWith(['verify', 'a.cs'], files).code, ExitCode.USAGE);
  assert.equal(runWith(['strip', 'a.cs'], files).code, ExitCode.USAGE);
  assert.equal(runWith(['count', '--dialect', 'cobol', 'a.cs'], files).code, ExitCode.USAGE);
});

test('--dialect overrides the extension and --json prints machine output', () => {
  const files = { 'a.txt': '// one\nint a;' };
  const result = runWith(['count', 'a.txt', '--dialect', Dialect.CSHARP, '--json'], files);
  assert.equal(result.code, ExitCode.COMMENTS_ONLY);
  const [entry] = JSON.parse(result.out);
  assert.equal(entry.units, 1);
  assert.equal(entry.dialect, Dialect.CSHARP);
});

test('parseArguments and resolveDialect', () => {
  assert.deepEqual(parseArguments(['count', 'x.ts', '--json']), {
    command: 'count', json: true, dialect: null, files: ['x.ts'],
  });
  assert.equal(resolveDialect(null, ['base.tmp', 'edited.component.html']), Dialect.HTML);
  assert.equal(resolveDialect(null, ['base.ts', 'edited.tmp']), Dialect.SCRIPT);
  assert.throws(() => resolveDialect(null, ['a.tsx']), /unsupported file type/);
});

test('decodeSource reads UTF-8 with a BOM and UTF-16 LE with a BOM', () => {
  assert.equal(decodeSource(Buffer.from([0xef, 0xbb, 0xbf, 0x61])), 'a');
  assert.equal(decodeSource(Buffer.from([0xff, 0xfe, 0x61, 0x00])), 'a');
});

test('the script runs end to end on real files', () => {
  const folder = mkdtempSync(join(tmpdir(), 'comment-diff-'));
  try {
    const baseline = join(folder, 'baseline.ts');
    const edited = join(folder, 'edited.ts');
    writeFileSync(baseline, '﻿// restates the line\nconst url = "http://a"; // c\n');
    writeFileSync(edited, 'const url = "http://a";\n');
    const result = spawnSync(process.execPath, [SCRIPT, 'verify', baseline, edited], { encoding: 'utf8' });
    assert.equal(result.status, ExitCode.COMMENTS_ONLY, result.stderr);
    assert.match(result.stdout, /^PASS/);
  } finally {
    rmSync(folder, { recursive: true, force: true });
  }
});
