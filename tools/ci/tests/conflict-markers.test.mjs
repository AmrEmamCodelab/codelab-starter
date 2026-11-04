// Run: node --test "tools/ci/tests/**/*.test.mjs"
// Markers are built at runtime so this file never contains one.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import { EXIT_CLEAN, EXIT_FINDINGS, EXIT_USAGE, findConflictMarkers, main, parseArguments, scanText } from '../conflict-markers.mjs';

const START = '<'.repeat(7);
const BASE = '|'.repeat(7);
const SEPARATOR = '='.repeat(7);
const END = '>'.repeat(7);
const SCRIPT = fileURLToPath(new URL('../conflict-markers.mjs', import.meta.url));

const tempRoots = [];
function tempRoot(files) {
  const root = mkdtempSync(path.join(tmpdir(), 'conflict-markers-'));
  tempRoots.push(root);
  for (const [relative, content] of Object.entries(files)) {
    const full = path.join(root, relative);
    mkdirSync(path.dirname(full), { recursive: true });
    writeFileSync(full, content);
  }
  return root;
}

afterEach(() => {
  while (tempRoots.length > 0) rmSync(tempRoots.pop(), { recursive: true, force: true });
});

describe('scanText', () => {
  it('reports every marker line of a conflict block', () => {
    const text = `a\n${START} HEAD\nours\n${BASE} base\nold\n${SEPARATOR}\ntheirs\n${END} main\n`;
    assert.deepEqual(scanText(text), [
      { line: 2, marker: START },
      { line: 4, marker: BASE },
      { line: 6, marker: SEPARATOR },
      { line: 8, marker: END },
    ]);
  });

  it('handles CRLF line endings', () => {
    assert.equal(scanText(`${START}\r\na\r\n${SEPARATOR}\r\nb\r\n${END}\r\n`).length, 3);
  });

  for (const lookalike of [`Title\n${SEPARATOR}\n`, `text ${START} mid-line\n`, `${START}<\n`, `${START}HEAD\n`]) {
    it(`ignores the lookalike ${JSON.stringify(lookalike)}`, () => {
      assert.deepEqual(scanText(lookalike), []);
    });
  }
});

describe('findConflictMarkers', () => {
  it('finds markers in any text file and skips binaries and excluded folders', () => {
    const conflict = `${START} HEAD\na\n${SEPARATOR}\nb\n${END} main\n`;
    const root = tempRoot({
      'src/app.cs': conflict,
      'image.bin': Buffer.concat([Buffer.from([0, 1, 2]), Buffer.from(conflict)]),
      'node_modules/pkg/index.js': conflict,
      'clean.md': '# Clean\n',
    });

    const files = [...new Set(findConflictMarkers(root).map((finding) => finding.file))];

    assert.deepEqual(files, ['src/app.cs']);
  });

  it('honours .gitignore inside a git work tree', (context) => {
    const root = tempRoot({ '.gitignore': 'scratch/\n', 'scratch/notes.txt': `${START} HEAD\n` });
    const init = spawnSync('git', ['init', '--quiet'], { cwd: root });
    if (init.error || init.status !== 0) {
      context.skip('git is not available');
      return;
    }
    assert.deepEqual(findConflictMarkers(root), []);
  });
});

describe('command line', () => {
  it('parses --root and rejects anything else', () => {
    assert.deepEqual(parseArguments(['--root', 'x']), { root: 'x' });
    assert.deepEqual(parseArguments([]), { root: '.' });
    assert.ok(parseArguments(['--rot', 'x']).error);
  });

  it('returns the documented exit codes', () => {
    const silent = () => {};
    const clean = tempRoot({ 'a.md': '# A\n' });
    const dirty = tempRoot({ 'a.md': `${START} HEAD\n` });

    assert.equal(main(['--root', clean], silent, silent), EXIT_CLEAN);
    assert.equal(main(['--root', dirty], silent, silent), EXIT_FINDINGS);
    assert.equal(main(['--bad'], silent, silent), EXIT_USAGE);
    assert.equal(main(['--root', path.join(clean, 'missing')], silent, silent), EXIT_USAGE);
  });

  it('sets the process exit code when run as a script', () => {
    const dirty = tempRoot({ 'a.md': `${START} HEAD\n` });
    const run = spawnSync(process.execPath, [SCRIPT, '--root', dirty], { encoding: 'utf8' });
    assert.equal(run.status, EXIT_FINDINGS);
    assert.match(run.stdout, /a\.md:1:/);
  });
});
