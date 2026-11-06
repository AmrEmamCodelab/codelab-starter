// Run: node --test "tools/ci/tests/**/*.test.mjs"
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  DEFAULT_MARKERS,
  DEV_AUTOFILL_MARKER,
  DEV_AUTOFILL_SET_MARKER,
  EXIT_CLEAN,
  EXIT_FINDINGS,
  EXIT_USAGE,
  findDevAutofillMarkers,
  isScannedFile,
  listBundleFiles,
  main,
  parseArguments,
  scanText,
} from '../no-dev-autofill.mjs';

const SCRIPT = fileURLToPath(new URL('../no-dev-autofill.mjs', import.meta.url));
const REPO_ROOT = fileURLToPath(new URL('../../..', import.meta.url));
const DEV_AUTOFILL_FOLDER = path.join(REPO_ROOT, 'frontend/starter-web/src/app/shared/dev-autofill');
const CLEAN_MAIN = 'console.log("app");function a(){return"shared.file-picker.choose"}';
const LEAKED_MAIN = `var m="${DEV_AUTOFILL_MARKER}";`;
const LEAKED_SET = 'const s={formKey:"test-record-form",labelKey:"testRecords.devAutofill.valid"};';

const tempRoots = [];
function tempDist(files) {
  const root = mkdtempSync(path.join(tmpdir(), 'no-dev-autofill-'));
  tempRoots.push(root);
  for (const [relative, content] of Object.entries(files)) {
    const full = path.join(root, relative);
    mkdirSync(path.dirname(full), { recursive: true });
    writeFileSync(full, content);
  }
  return root;
}
const quiet = () => {};
function capture() {
  const lines = [];
  return { lines, log: (line) => lines.push(line) };
}

afterEach(() => {
  while (tempRoots.length > 0) rmSync(tempRoots.pop(), { recursive: true, force: true });
});

describe('markers', () => {
  it('equal the real implementation constant and the label-key segment', () => {
    const impl = readFileSync(path.join(DEV_AUTOFILL_FOLDER, 'dev-autofill.impl.ts'), 'utf8');
    const keys = readFileSync(path.join(DEV_AUTOFILL_FOLDER, 'dev-autofill-keys.ts'), 'utf8');
    assert.match(impl, new RegExp(`export const DEV_AUTOFILL_MARKER = '${DEV_AUTOFILL_MARKER}';`));
    assert.match(keys, new RegExp(`export const DEV_AUTOFILL_LABEL_SEGMENT = '${DEV_AUTOFILL_SET_MARKER.slice(1, -1)}';`));
  });

  it('never appear in the production stubs', () => {
    for (const file of ['dev-autofill.stub.ts', 'dev-autofill.sets.stub.ts']) {
      const text = readFileSync(path.join(DEV_AUTOFILL_FOLDER, file), 'utf8');
      assert.deepEqual(scanText(text), [], file);
    }
  });
});

describe('scanText', () => {
  it('finds each default marker', () => {
    assert.deepEqual(scanText(LEAKED_MAIN), [DEV_AUTOFILL_MARKER]);
    assert.deepEqual(scanText(LEAKED_SET), [DEV_AUTOFILL_SET_MARKER]);
    assert.deepEqual(scanText(CLEAN_MAIN), []);
  });

  it('ignores the stub selectors and an unrelated camelCase key', () => {
    assert.deepEqual(scanText('selectors:[["dga-dev-autofill"]],[["","devAutofill",""]];"a.devAutofillOff"'), []);
  });

  it('uses the markers it is given', () => {
    assert.deepEqual(scanText('a sample value', ['sample value']), ['sample value']);
  });
});

describe('isScannedFile and listBundleFiles', () => {
  it('reads bundle files only, in nested folders, sorted', () => {
    const dist = tempDist({
      'index.html': '<html></html>',
      'main-ABC.js': CLEAN_MAIN,
      'chunk/b.mjs': '',
      'styles.CSS': '',
      'assets/i18n/en.json': '{"shared":{"dev-autofill":{"title":"Developer autofill"}}}',
      'favicon.ico': '',
    });
    assert.deepEqual(listBundleFiles(dist), ['chunk/b.mjs', 'index.html', 'main-ABC.js', 'styles.CSS']);
    assert.equal(isScannedFile('en.json'), false);
  });
});

describe('findDevAutofillMarkers', () => {
  it('is empty for a clean dist', () => {
    assert.deepEqual(findDevAutofillMarkers(tempDist({ 'main.js': CLEAN_MAIN, 'index.html': '<app-root></app-root>' })), []);
  });

  it('names the file and marker of every leak', () => {
    const dist = tempDist({ 'main.js': LEAKED_MAIN, 'lazy/chunk-1.js': LEAKED_SET });
    assert.deepEqual(findDevAutofillMarkers(dist), [
      { file: 'lazy/chunk-1.js', marker: DEV_AUTOFILL_SET_MARKER },
      { file: 'main.js', marker: DEV_AUTOFILL_MARKER },
    ]);
  });
});

describe('parseArguments', () => {
  it('needs --dist and adds extra markers after the defaults, once each', () => {
    assert.deepEqual(parseArguments(['--dist', 'out', '--marker', 'x', '--marker', DEV_AUTOFILL_MARKER]), {
      dist: 'out',
      markers: [...DEFAULT_MARKERS, 'x'],
    });
  });

  it('refuses a missing dist, a missing value, an unknown option and a second dist', () => {
    for (const argv of [[], ['--dist'], ['--dist', ''], ['--root', 'x'], ['--dist', 'a', '--dist', 'b'], ['--marker', 'x']]) {
      assert.ok(parseArguments(argv).error, JSON.stringify(argv));
    }
  });
});

describe('main', () => {
  it('passes a clean dist', () => {
    const output = capture();
    assert.equal(main(['--dist', tempDist({ 'main.js': CLEAN_MAIN })], output.log, quiet), EXIT_CLEAN);
    assert.match(output.lines.at(-1), /PASSED \(1 file\(s\), 2 marker\(s\)\)/);
  });

  it('fails a dist with the marker and reports each finding', () => {
    const output = capture();
    assert.equal(main(['--dist', tempDist({ 'main.js': LEAKED_MAIN })], output.log, quiet), EXIT_FINDINGS);
    assert.deepEqual(output.lines, [
      `main.js: DevAutofill marker '${DEV_AUTOFILL_MARKER}' in a production bundle`,
      'no dev autofill: FAILED with 1 finding(s)',
    ]);
  });

  it('fails on an extra marker', () => {
    const dist = tempDist({ 'main.js': 'title:"Sample request title"' });
    assert.equal(main(['--dist', dist, '--marker', 'Sample request title'], quiet, quiet), EXIT_FINDINGS);
  });

  it('is a usage error for bad arguments, a missing folder and a folder without bundle files', () => {
    assert.equal(main([], quiet, quiet), EXIT_USAGE);
    assert.equal(main(['--dist', path.join(tmpdir(), 'no-dev-autofill-missing-folder')], quiet, quiet), EXIT_USAGE);
    assert.equal(main(['--dist', tempDist({ 'assets/en.json': '{}' })], quiet, quiet), EXIT_USAGE);
  });

  it('runs as a script with the exit code', () => {
    const clean = spawnSync(process.execPath, [SCRIPT, '--dist', tempDist({ 'main.js': CLEAN_MAIN })], { encoding: 'utf8' });
    assert.equal(clean.status, EXIT_CLEAN, clean.stderr);
    const leaked = spawnSync(process.execPath, [SCRIPT, '--dist', tempDist({ 'main.js': LEAKED_SET })], { encoding: 'utf8' });
    assert.equal(leaked.status, EXIT_FINDINGS, leaked.stderr);
  });
});
