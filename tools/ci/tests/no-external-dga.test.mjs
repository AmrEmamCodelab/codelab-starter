// Run: node --test "tools/ci/tests/**/*.test.mjs"
// The external DGA scopes are assembled at run time, so this file never contains one (the scrub sweep would refuse it).
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { afterEach, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

import {
  EXIT_CLEAN,
  EXIT_FINDINGS,
  EXIT_USAGE,
  findExternalDgaReferences,
  isSearchedFile,
  listSearchedFiles,
  main,
  parseArguments,
  scanText,
} from '../no-external-dga.mjs';

const AT = '@';
const DEV_SCOPE = `${AT}dev-dga`;
const SHORT_SCOPE = `${AT}dga/`;
const SCRIPT = fileURLToPath(new URL('../no-external-dga.mjs', import.meta.url));
const REPO_ROOT = fileURLToPath(new URL('../../..', import.meta.url));

const tempRoots = [];
function tempRoot(files) {
  const root = mkdtempSync(path.join(tmpdir(), 'no-external-dga-'));
  tempRoots.push(root);
  for (const [relative, content] of Object.entries(files)) {
    const full = path.join(root, relative);
    mkdirSync(path.dirname(full), { recursive: true });
    writeFileSync(full, content);
  }
  return root;
}
const quiet = () => {};

afterEach(() => {
  while (tempRoots.length > 0) rmSync(tempRoots.pop(), { recursive: true, force: true });
});

describe('scanText', () => {
  it('finds the dev scope and the short scope as a package path, with 1-based lines', () => {
    const text = `import { A } from '${DEV_SCOPE}/angular';\nok\n@use '${SHORT_SCOPE}ui/styles';\n`;
    assert.deepEqual(scanText(text), [{ line: 1, scope: DEV_SCOPE }, { line: 3, scope: SHORT_SCOPE }]);
  });

  it('ignores our own package, the DGA domain and the bare word', () => {
    assert.deepEqual(scanText("import { DgaButton } from '@codelab/ng-dga';\nhttps://dga.gov.sa\nthe dga look\n"), []);
  });

  it('matches regardless of case', () => {
    assert.equal(scanText(`"${DEV_SCOPE.toUpperCase()}/angular": "1.0.0"`).length, 1);
  });
});

describe('isSearchedFile', () => {
  it('reads manifests, lockfiles, angular.json, .scss and .ts only', () => {
    for (const name of ['package.json', 'package-lock.json', 'angular.json', 'a.scss', 'a.component.ts']) assert.equal(isSearchedFile(name), true, name);
    for (const name of ['README.md', 'a.html', 'a.css', 'a.mjs', 'tsconfig.json']) assert.equal(isSearchedFile(name), false, name);
  });
});

describe('findExternalDgaReferences', () => {
  it('reports each reference in frontend/, packages/npm/ and the root manifest and lockfile', () => {
    const root = tempRoot({
      'package-lock.json': `{ "packages": { "node_modules/${DEV_SCOPE}/angular": {} } }`,
      'frontend/web/angular.json': `{ "styles": ["node_modules/${SHORT_SCOPE}ui/theme.css"] }`,
      'frontend/web/src/styles.scss': `@use '${SHORT_SCOPE}ui';\n`,
      'packages/npm/kit/package.json': `{ "dependencies": { "${DEV_SCOPE}/angular": "1.0.0" } }`,
      'packages/npm/kit/src/a.ts': "export const ok = '@codelab/ng-dga';\n",
    });
    assert.deepEqual(findExternalDgaReferences(root).map(({ file, line }) => `${file}:${line}`), [
      'frontend/web/angular.json:1',
      'frontend/web/src/styles.scss:1',
      'package-lock.json:1',
      'packages/npm/kit/package.json:1',
    ]);
  });

  it('skips dependency and build folders, other folders and other file types', () => {
    const root = tempRoot({
      'frontend/web/node_modules/x/package.json': `{ "name": "${DEV_SCOPE}/angular" }`,
      'frontend/web/dist/main.ts': `import '${DEV_SCOPE}/angular';`,
      'frontend/web/src/notes.md': `${DEV_SCOPE}`,
      'docs/adr/ADR-001.ts': `${DEV_SCOPE}`,
      'packages/dotnet/x/a.ts': `${DEV_SCOPE}`,
    });
    assert.deepEqual(findExternalDgaReferences(root), []);
    assert.deepEqual(listSearchedFiles(root), []);
  });
});

describe('main and the command line', () => {
  it('parses an optional --root and refuses anything else', () => {
    assert.deepEqual(parseArguments([]), { root: '.' });
    assert.deepEqual(parseArguments(['--root', 'x']), { root: 'x' });
    assert.match(parseArguments(['--folder', 'x']).error, /Usage/);
  });

  it('returns clean, findings and usage exit codes', () => {
    const clean = tempRoot({ 'frontend/web/package.json': '{ "dependencies": { "@codelab/ng-dga": "1.0.0" } }' });
    assert.equal(main(['--root', clean], quiet, quiet), EXIT_CLEAN);
    const dirty = tempRoot({ 'frontend/web/src/a.ts': `import '${SHORT_SCOPE}ui';` });
    const lines = [];
    assert.equal(main(['--root', dirty], (line) => lines.push(line), quiet), EXIT_FINDINGS);
    assert.match(lines[0], /^frontend\/web\/src\/a\.ts:1: external DGA package scope/);
    assert.equal(main(['--root', path.join(dirty, 'missing')], quiet, quiet), EXIT_USAGE);
    assert.equal(main(['--x'], quiet, quiet), EXIT_USAGE);
  });

  it('runs as a script', () => {
    const dirty = tempRoot({ 'packages/npm/kit/package.json': `{ "dependencies": { "${DEV_SCOPE}/angular": "1" } }` });
    assert.equal(spawnSync(process.execPath, [SCRIPT, '--root', dirty], { encoding: 'utf8' }).status, EXIT_FINDINGS);
  });

  it('passes on this repository (phase 4 exit criterion)', () => {
    const findings = findExternalDgaReferences(REPO_ROOT);
    assert.deepEqual(findings, []);
  });
});
